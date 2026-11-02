import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError, api } from '../../api/client';
import type { BucketDomains, R2Bucket, R2CustomDomain, StorageOptions } from '../../api/types';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, Checkbox, DataTable, Dialog, Input, Select, Switch, type DataColumn } from '../../ui';

/** Cloudflare's bucket-name rule, checked here so the form can say so before a round trip. The server checks it again. */
export function bucketNameProblem(name: string): string | null {
  const n = name.trim();
  if (!n) return 'Bucket name is required.';
  if (n.length < 3 || n.length > 63) return 'A bucket name is 3 to 63 characters.';
  if (!/^[a-z0-9][a-z0-9-]*[a-z0-9]$/.test(n)) return 'Lowercase letters, digits and hyphens; start and end with a letter or digit.';
  return null;
}

/** A full hostname. The zone it belongs to is worked out on the server from what the token can see. */
export function domainProblem(domain: string): string | null {
  const d = domain.trim().replace(/\.$/, '').toLowerCase();
  if (!d) return 'Hostname is required.';
  if (!d.includes('.') || !/^(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))+$/.test(d)) return 'Enter a full hostname, like files.example.com.';
  return null;
}

const LOCATION_NAMES: Record<string, string> = {
  wnam: 'Western North America', enam: 'Eastern North America', weur: 'Western Europe', eeur: 'Eastern Europe', apac: 'Asia-Pacific', oc: 'Oceania',
};

function statusTone(status: string | null): 'success' | 'warning' | 'danger' | 'muted' {
  switch (status) {
    case 'active': return 'success';
    case 'pending': case 'initializing': return 'warning';
    case 'error': case 'blocked': case 'deactivated': return 'danger';
    default: return 'muted';
  }
}

export function StoragePage() {
  useDocumentTitle('R2 Storage');
  const notify = useToast();
  const navigate = useNavigate();
  const [buckets, setBuckets] = useState<R2Bucket[] | null>(null);
  const [options, setOptions] = useState<StorageOptions | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [deleting, setDeleting] = useState<R2Bucket | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const [list, opts] = await Promise.all([api.storage.buckets(), api.storage.options()]);
      setBuckets(list.buckets);
      setOptions(opts);
      setError(null);
    } catch (err) {
      setError(err as ApiError);
      setBuckets([]);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const remove = async () => {
    if (!deleting) return;
    setBusy(true);
    try {
      await api.storage.deleteBucket(deleting.name);
      notify(`Bucket ${deleting.name} deleted`, 'success');
      if (selected === deleting.name) setSelected(null);
      setDeleting(null);
      await load();
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const columns: DataColumn<R2Bucket>[] = [
    { key: 'name', label: 'Bucket', sortable: true, render: (b) => <span className="sz-strong sz-mono">{b.name}</span> },
    { key: 'location', label: 'Location', width: 190, render: (b) => (b.location ? LOCATION_NAMES[b.location] ?? b.location : <span className="sz-muted">Automatic</span>) },
    { key: 'storageClass', label: 'Class', width: 150, render: (b) => b.storageClass === 'InfrequentAccess' ? 'Infrequent access' : 'Standard' },
    { key: 'createdAt', label: 'Created', width: 130, render: (b) => (b.createdAt ? new Date(b.createdAt).toLocaleDateString() : '-') },
    {
      key: 'actions', label: 'Actions', width: 200,
      render: (b) => (
        <div className="sz-row" style={{ flexWrap: 'nowrap' }}>
          <Button variant={selected === b.name ? 'outline' : 'ghost'} size="sm" onClick={() => setSelected(selected === b.name ? null : b.name)}>Domains</Button>
          <Button variant="ghost" size="sm" disabled={busy} onClick={() => setDeleting(b)}>Delete</Button>
        </div>
      ),
    },
  ];

  if (buckets === null) return <Loading />;

  // A missing or account-less token is a 409 whose title says what to fix on Settings.
  const needsSettings = error?.status === 409;

  return (
    <div className="sz-page">
      <PageHeader title="R2 Storage" actions={<Button disabled={!!error} onClick={() => setCreating(true)}>Create bucket</Button>}>
        Buckets on your Cloudflare account, and the hostnames in front of them. Uses the token saved in Settings; it needs
        the account permission <strong>Workers R2 Storage → Edit</strong>.
      </PageHeader>

      {error ? (
        <Notice tone={needsSettings ? 'warning' : 'danger'} title={needsSettings ? 'Storage needs the Cloudflare token' : "Couldn't reach R2"}
          action={needsSettings ? <Button size="sm" onClick={() => navigate('/settings')}>Open Settings</Button> : <Button size="sm" variant="outline" onClick={() => void load()}>Try again</Button>}>
          {error.message}
        </Notice>
      ) : null}

      {!error ? (
        <Card>
          <DataTable caption="R2 buckets" density="standard" columns={columns} rows={buckets} rowKey={(b) => b.name}
            empty='No buckets yet. Use "Create bucket" to make your first one.' />
        </Card>
      ) : null}

      {selected && !error ? <DomainsCard key={selected} bucket={selected} options={options} /> : null}

      <CreateBucketDialog open={creating} options={options} onClose={() => setCreating(false)}
        onCreated={async (b) => { setCreating(false); notify(`Bucket ${b.name} created`, 'success'); await load(); }} />

      <ConfirmDialog open={deleting !== null} busy={busy} title="Delete bucket"
        message={deleting ? `Delete the bucket '${deleting.name}'? Cloudflare only deletes an empty bucket; its custom domains stop serving.` : ''}
        onCancel={() => setDeleting(null)} onConfirm={() => void remove()} />
    </div>
  );
}

function CreateBucketDialog({ open, options, onClose, onCreated }: {
  open: boolean;
  options: StorageOptions | null;
  onClose: () => void;
  onCreated: (bucket: R2Bucket) => void | Promise<void>;
}) {
  const [name, setName] = useState('');
  const [location, setLocation] = useState('');
  const [storageClass, setStorageClass] = useState('');
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  const nameProblem = bucketNameProblem(name);

  const create = async () => {
    setTouched(true);
    if (nameProblem) return;
    setBusy(true);
    setProblem(null);
    try {
      const created = await api.storage.createBucket({ name: name.trim(), locationHint: location || null, storageClass: storageClass || null });
      setName(''); setLocation(''); setStorageClass(''); setTouched(false);
      await onCreated(created);
    } catch (err) {
      setProblem((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} title="Create bucket"
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={() => void create()} disabled={busy}>{busy ? 'Creating…' : 'Create bucket'}</Button>
      </>}>
      <div className="sz-stack">
        <Input label="Bucket name" placeholder="backups" value={name} disabled={busy} autoFocus
          onChange={(e) => setName(e.target.value)} onBlur={() => setTouched(true)}
          error={touched && nameProblem ? nameProblem : undefined}
          hint={!touched || !nameProblem ? '3 to 63 characters: lowercase letters, digits and hyphens. The name cannot be changed later.' : undefined} />
        <Select label="Location" value={location} disabled={busy} onChange={(e) => setLocation(e.target.value)}>
          <option value="">Let Cloudflare choose</option>
          {(options?.locationHints ?? []).map((h) => <option key={h} value={h}>{LOCATION_NAMES[h] ?? h}</option>)}
        </Select>
        <span className="sz-xs sz-muted">A hint, not a guarantee: Cloudflare places the bucket near where it is first used.</span>
        <Select label="Storage class" value={storageClass} disabled={busy} onChange={(e) => setStorageClass(e.target.value)}>
          <option value="">Standard</option>
          {(options?.storageClasses ?? []).filter((c) => c !== 'Standard').map((c) => <option key={c} value={c}>{c === 'InfrequentAccess' ? 'Infrequent access' : c}</option>)}
        </Select>
        {problem ? <Notice tone="danger" title="The bucket was not created">{problem}</Notice> : null}
      </div>
    </Dialog>
  );
}

function DomainsCard({ bucket, options }: { bucket: string; options: StorageOptions | null }) {
  const notify = useToast();
  const [data, setData] = useState<BucketDomains | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [domain, setDomain] = useState('');
  const [enabled, setEnabled] = useState(true);
  const [minTls, setMinTls] = useState('');
  const [touched, setTouched] = useState(false);
  const [addProblem, setAddProblem] = useState<string | null>(null);
  const [removing, setRemoving] = useState<R2CustomDomain | null>(null);

  const load = useCallback(async () => {
    try {
      setData(await api.storage.domains(bucket));
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }, [bucket]);

  useEffect(() => { void load(); }, [load]);

  const run = async (key: string, work: () => Promise<void>) => {
    setBusy(key);
    try { await work(); await load(); } catch (err) { notify((err as Error).message, 'error'); } finally { setBusy(null); }
  };

  const problem = domainProblem(domain);

  const add = async () => {
    setTouched(true);
    if (problem) return;
    setBusy('add');
    setAddProblem(null);
    try {
      const added = await api.storage.addDomain(bucket, { domain: domain.trim(), enabled, minTls: minTls || null });
      notify(`${added.domain} attached to ${bucket}`, 'success');
      setDomain(''); setTouched(false);
      await load();
    } catch (err) {
      setAddProblem((err as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const columns: DataColumn<R2CustomDomain>[] = [
    { key: 'domain', label: 'Hostname', render: (d) => <span className="sz-strong sz-mono">{d.domain}</span> },
    { key: 'zoneName', label: 'Zone', width: 170, render: (d) => d.zoneName ?? d.zoneId ?? '-' },
    {
      key: 'status', label: 'Status', width: 210,
      render: (d) => (
        <span className="sz-row" style={{ gap: 6 }}>
          <Badge tone={statusTone(d.ownershipStatus)} title="Ownership">{d.ownershipStatus ?? 'unknown'}</Badge>
          <Badge tone={statusTone(d.sslStatus)} title="Certificate">TLS {d.sslStatus ?? 'unknown'}</Badge>
        </span>
      ),
    },
    { key: 'minTls', label: 'Min TLS', width: 90, render: (d) => d.minTls ?? '1.0' },
    {
      key: 'enabled', label: 'Serving', width: 90,
      render: (d) => (
        <span title={d.enabled ? 'Serving: switch off to pause' : 'Paused: switch on to serve'}>
          <Switch checked={d.enabled} disabled={busy !== null}
            onChange={(next) => void run(d.domain, () => api.storage.updateDomain(bucket, d.domain, next, d.minTls).then(() => undefined))} />
        </span>
      ),
    },
    { key: 'actions', label: '', width: 100, render: (d) => <Button variant="ghost" size="sm" disabled={busy !== null} onClick={() => setRemoving(d)}>Remove</Button> },
  ];

  return (
    <Card>
      <div className="sz-stack">
        <div className="sz-card-header">
          <h2>Domains for <span className="sz-mono">{bucket}</span></h2>
        </div>

        {error ? <Notice tone="danger" title="Couldn't load the domains" action={<Button size="sm" variant="outline" onClick={() => void load()}>Try again</Button>}>{error}</Notice> : null}
        {!data && !error ? <Loading /> : null}

        {data ? (
          <>
            <div className="sz-row-between">
              <div className="sz-stack-sm">
                <span className="sz-strong">Public r2.dev access</span>
                <span className="sz-small sz-muted">
                  {data.managed.enabled
                    ? <>Anyone can read this bucket at <span className="sz-mono">{data.managed.domain}</span>. Rate-limited and meant for testing; use a custom domain for real traffic.</>
                    : <>Off. <span className="sz-mono">{data.managed.domain}</span> is not reachable from the public internet.</>}
                </span>
              </div>
              <label className="sz-row" style={{ gap: 8 }}>
                <span className="sz-visually-hidden">Public r2.dev access</span>
                <Switch checked={data.managed.enabled} disabled={busy !== null}
                  onChange={(next) => void run('managed', () => api.storage.setPublicAccess(bucket, next).then(() => undefined))} />
              </label>
            </div>

            <h3>Custom domains</h3>
            <p className="sz-small sz-muted">
              A hostname in one of your zones that serves this bucket. Cloudflare writes the DNS record and issues the
              certificate; ownership and TLS read <em>active</em> once both are done.
            </p>
            <DataTable caption={`Custom domains on ${bucket}`} density="standard" columns={columns} rows={data.custom} rowKey={(d) => d.domain}
              empty="No custom domains. Attach one below." />

            <h3>Attach a domain</h3>
            <div className="sz-grid">
              <Input label="Hostname" placeholder="files.example.com" value={domain} disabled={busy !== null}
                onChange={(e) => setDomain(e.target.value)} onBlur={() => setTouched(true)}
                error={touched && problem ? problem : undefined}
                hint={!touched || !problem ? (data.zones.length ? `In one of your zones: ${data.zones.map((z) => z.name).join(', ')}` : 'The token sees no zones; add Zone → Zone → Read to it.') : undefined} />
              <Select label="Minimum TLS" value={minTls} disabled={busy !== null} onChange={(e) => setMinTls(e.target.value)}>
                <option value="">Cloudflare default (1.0)</option>
                {(options?.minTlsVersions ?? []).map((v) => <option key={v} value={v}>TLS {v}</option>)}
              </Select>
            </div>
            <Checkbox label="Start serving as soon as the certificate is ready" checked={enabled} disabled={busy !== null} onChange={(e) => setEnabled(e.target.checked)} />
            {addProblem ? <Notice tone="danger" title="The domain was not attached">{addProblem}</Notice> : null}
            <div className="sz-row">
              <Button disabled={busy !== null} onClick={() => void add()}>{busy === 'add' ? 'Attaching…' : 'Attach domain'}</Button>
            </div>
          </>
        ) : null}
      </div>

      <ConfirmDialog open={removing !== null} busy={busy === 'remove'} title="Remove custom domain" confirmLabel="Remove"
        message={removing ? `Remove '${removing.domain}' from '${bucket}'? Cloudflare deletes its DNS record; the bucket and its objects are untouched.` : ''}
        onCancel={() => setRemoving(null)}
        onConfirm={() => { const d = removing; if (!d) return; void run('remove', async () => { await api.storage.removeDomain(bucket, d.domain); setRemoving(null); notify(`${d.domain} removed`, 'success'); }); }} />
    </Card>
  );
}
