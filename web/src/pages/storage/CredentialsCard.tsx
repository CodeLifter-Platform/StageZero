import { useCallback, useEffect, useState } from 'react';
import { ApiError, api } from '../../api/client';
import type { AppCredential, CreatedCredential, R2Bucket, StorageOptions } from '../../api/types';
import { formatDateTime } from '../../app/format';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { Badge, Button, Card, Checkbox, DataTable, Dialog, Input, Segmented, Select, type DataColumn } from '../../ui';

/** Cloudflare's rule, checked here so the form can say so first. The server checks it again. */
export function credentialNameProblem(name: string): string | null {
  const n = name.trim();
  if (!n) return 'Credential name is required.';
  if (n.length > 120) return 'A credential name is at most 120 characters.';
  return null;
}

const EXPIRY_CHOICES: Array<{ value: string; label: string }> = [
  { value: '', label: 'Never' },
  { value: '30', label: '30 days' },
  { value: '90', label: '90 days' },
  { value: '365', label: '1 year' },
];

function accessLabel(access: string): string {
  switch (access) {
    case 'read': return 'Read';
    case 'readwrite': return 'Read & write';
    case 'admin': return 'Admin';
    default: return access;
  }
}

/**
 * Per-app R2 credentials: one account API token per app, scoped to the buckets it may touch.
 * Cloudflare returns the token value once; StageZero derives the S3 pair from it, shows all of
 * it in one dialog, and keeps none of it.
 */
export function CredentialsCard({ buckets, options }: { buckets: R2Bucket[]; options: StorageOptions | null }) {
  const notify = useToast();
  const [credentials, setCredentials] = useState<AppCredential[] | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [creating, setCreating] = useState(false);
  const [created, setCreated] = useState<CreatedCredential | null>(null);
  const [rolling, setRolling] = useState<AppCredential | null>(null);
  const [revoking, setRevoking] = useState<AppCredential | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setCredentials((await api.storage.credentials()).credentials);
      setError(null);
    } catch (err) {
      setError(err as ApiError);
      setCredentials([]);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const roll = async () => {
    if (!rolling) return;
    setBusy(true);
    try {
      const result = await api.storage.rollCredential(rolling.id);
      setRolling(null);
      setCreated(result);
      await load();
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const revoke = async () => {
    if (!revoking) return;
    setBusy(true);
    try {
      await api.storage.deleteCredential(revoking.id);
      notify(`Credential ${revoking.name} revoked`, 'success');
      setRevoking(null);
      await load();
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setBusy(false);
    }
  };

  const columns: DataColumn<AppCredential>[] = [
    { key: 'name', label: 'Credential', sortable: true, render: (c) => <span className="sz-strong">{c.name}</span> },
    { key: 'access', label: 'Access', width: 120, render: (c) => <Badge tone={c.access === 'read' ? 'muted' : c.access === 'admin' ? 'warning' : 'success'}>{accessLabel(c.access)}</Badge> },
    {
      key: 'buckets', label: 'Buckets', width: 220,
      render: (c) => <span className="sz-mono sz-small">{c.buckets.length ? c.buckets.map((b) => (b.name === '*' ? 'all buckets' : b.name)).join(', ') : '-'}</span>,
    },
    { key: 'expiresOn', label: 'Expires', width: 150, render: (c) => (c.expiresOn ? formatDateTime(c.expiresOn) : <span className="sz-muted">Never</span>) },
    { key: 'lastUsedOn', label: 'Last used', width: 150, render: (c) => (c.lastUsedOn ? formatDateTime(c.lastUsedOn) : <span className="sz-muted">Not yet</span>) },
    {
      key: 'actions', label: 'Actions', width: 170,
      render: (c) => (
        <div className="sz-row" style={{ flexWrap: 'nowrap' }}>
          <Button variant="ghost" size="sm" disabled={busy} onClick={() => setRolling(c)}>Roll</Button>
          <Button variant="ghost" size="sm" disabled={busy} onClick={() => setRevoking(c)}>Revoke</Button>
        </div>
      ),
    },
  ];

  // A 502 whose detail is Cloudflare refusing the token list means the saved token lacks the Account API Tokens row.
  const needsPermission = error !== null && (error.status === 502 || error.status === 409);

  return (
    <Card>
      <div className="sz-stack">
        <div className="sz-card-header">
          <h2>App credentials</h2>
          <Button size="sm" disabled={!!error || buckets.length === 0} onClick={() => setCreating(true)}>New credential</Button>
        </div>
        <p className="sz-small sz-muted">
          One credential per app, limited to the buckets it needs: an S3 access key and secret any R2 client takes.
          Cloudflare shows the secret once, so StageZero keeps none of it. Roll a credential to replace its secret;
          revoke it to cut the app off.
        </p>

        {error ? (
          <Notice tone={needsPermission ? 'warning' : 'danger'} title={needsPermission ? 'Credentials need one more permission' : "Couldn't list the credentials"}
            action={<Button size="sm" variant="outline" onClick={() => void load()}>Try again</Button>}>
            {needsPermission
              ? <>Add <strong>Account → Account API Tokens → Edit</strong> to the saved token in Cloudflare (editing keeps the same value), then try again. {error.message}</>
              : error.message}
          </Notice>
        ) : null}
        {credentials === null && !error ? <Loading /> : null}
        {credentials !== null && !error ? (
          <DataTable caption="Per-app R2 credentials" density="standard" columns={columns} rows={credentials} rowKey={(c) => c.id}
            empty={buckets.length ? 'No app credentials yet. Create one for each app that needs a bucket.' : 'Create a bucket first; a credential is scoped to buckets.'} />
        ) : null}
      </div>

      <CreateCredentialDialog open={creating} buckets={buckets} options={options} onClose={() => setCreating(false)}
        onCreated={async (c) => { setCreating(false); setCreated(c); await load(); }} />

      <CredentialSecretsDialog created={created} onDone={() => setCreated(null)} />

      <ConfirmDialog open={rolling !== null} busy={busy} title="Roll credential" confirmLabel="Roll"
        message={rolling ? `Roll '${rolling.name}'? Cloudflare issues a new secret and the current one stops working at once. Every copy of it has to be replaced.` : ''}
        onCancel={() => setRolling(null)} onConfirm={() => void roll()} />

      <ConfirmDialog open={revoking !== null} busy={busy} title="Revoke credential" confirmLabel="Revoke"
        message={revoking ? `Revoke '${revoking.name}'? Anything using it loses access to ${revoking.buckets.map((b) => b.name).join(', ') || 'its buckets'} immediately. The buckets and their files are untouched.` : ''}
        onCancel={() => setRevoking(null)} onConfirm={() => void revoke()} />
    </Card>
  );
}

function CreateCredentialDialog({ open, buckets, options, onClose, onCreated }: {
  open: boolean;
  buckets: R2Bucket[];
  options: StorageOptions | null;
  onClose: () => void;
  onCreated: (created: CreatedCredential) => void | Promise<void>;
}) {
  const [name, setName] = useState('');
  const [access, setAccess] = useState('readwrite');
  const [chosen, setChosen] = useState<string[]>([]);
  const [expiry, setExpiry] = useState('');
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);

  const nameProblem = credentialNameProblem(name);
  const bucketProblem = chosen.length === 0 ? 'Choose at least one bucket.' : null;
  const accessLevels = options?.accessLevels ?? ['read', 'readwrite'];

  const toggle = (bucket: string, on: boolean) => setChosen((c) => (on ? [...c, bucket] : c.filter((b) => b !== bucket)));

  const create = async () => {
    setTouched(true);
    if (nameProblem || bucketProblem) return;
    setBusy(true);
    setProblem(null);
    try {
      const result = await api.storage.createCredential({ name: name.trim(), buckets: chosen, access, expiresInDays: expiry ? Number(expiry) : null });
      setName(''); setChosen([]); setExpiry(''); setAccess('readwrite'); setTouched(false);
      await onCreated(result);
    } catch (err) {
      setProblem((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} title="New app credential"
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={() => void create()} disabled={busy}>{busy ? 'Creating…' : 'Create credential'}</Button>
      </>}>
      <div className="sz-stack">
        <Input label="Name" placeholder="Specter backups" value={name} disabled={busy} autoFocus
          onChange={(e) => setName(e.target.value)} onBlur={() => setTouched(true)}
          error={touched && nameProblem ? nameProblem : undefined}
          hint={!touched || !nameProblem ? 'Name it after the app that will hold it; this is how it appears in Cloudflare.' : undefined} />
        <div className="sz-stack-sm">
          <span className="sz-small sz-strong" id="credential-access-label">Access</span>
          <Segmented aria-labelledby="credential-access-label" value={access} onChange={setAccess}
            options={accessLevels.map((a) => ({ value: a, label: accessLabel(a) }))} />
          <span className="sz-xs sz-muted">Read lists and downloads; read & write also uploads and deletes. Neither can create or delete buckets.</span>
        </div>
        <fieldset className="sz-stack-sm" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend className="sz-small sz-strong">Buckets</legend>
          {buckets.map((b) => (
            <Checkbox key={b.name} label={b.name} checked={chosen.includes(b.name)} disabled={busy} onChange={(e) => toggle(b.name, e.target.checked)} />
          ))}
          {touched && bucketProblem ? <span className="sz-xs" style={{ color: 'var(--cl-danger-text)' }}>{bucketProblem}</span> : null}
        </fieldset>
        <Select label="Expires" value={expiry} disabled={busy} onChange={(e) => setExpiry(e.target.value)}>
          {EXPIRY_CHOICES.map((c) => <option key={c.value} value={c.value}>{c.label}</option>)}
        </Select>
        {problem ? <Notice tone="danger" title="The credential was not created">{problem}</Notice> : null}
      </div>
    </Dialog>
  );
}

/**
 * The one showing of a credential's secrets. The dialog cannot be dismissed by the backdrop
 * and closes only once the person says the values are saved; the same pattern as Access
 * service tokens.
 */
export function CredentialSecretsDialog({ created, onDone }: { created: CreatedCredential | null; onDone: () => void }) {
  const notify = useToast();
  const [visible, setVisible] = useState(false);
  const [saved, setSaved] = useState(false);

  const copy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      notify('Copied to clipboard', 'success');
    } catch {
      notify('Could not copy automatically. Select the value and copy it manually.', 'warning');
    }
  };

  const done = () => { setVisible(false); setSaved(false); onDone(); };
  const endpoints = created ? [...new Set(created.credential.buckets.map((b) => b.endpoint))] : [];

  return (
    <Dialog open={created !== null} title={`Credential ${created?.credential.name ?? ''}`} style={{ maxWidth: 680 }}
      footer={<>
        <Checkbox label="I have saved the secret" checked={saved} onChange={(e) => setSaved(e.target.checked)} />
        <Button onClick={done} disabled={!saved}>Done</Button>
      </>}>
      {created ? (
        <div className="sz-stack">
          <Notice tone="warning" title="This will not be shown again.">
            Cloudflare returns a token's value once, and StageZero stores none of this. Put it in the app's secure settings
            (or 1Password) now; if it is lost, roll the credential and update the app.
          </Notice>
          <p className="sz-small">
            For <strong>{created.credential.buckets.map((b) => b.name).join(', ')}</strong>, {accessLabel(created.credential.access).toLowerCase()}.
            An S3 client takes the access key, the secret and the endpoint; the token value is the same credential for Cloudflare's own API.
          </p>
          <SecretField label="S3 access key ID" value={created.accessKeyId} onCopy={copy} />
          <SecretField label="S3 secret access key" value={created.secretAccessKey} secret visible={visible} onToggle={() => setVisible((v) => !v)} onCopy={copy} />
          {endpoints.map((endpoint) => <SecretField key={endpoint} label={endpoints.length > 1 ? `Endpoint (${created.credential.buckets.filter((b) => b.endpoint === endpoint).map((b) => b.name).join(', ')})` : 'S3 endpoint'} value={endpoint} onCopy={copy} />)}
          <SecretField label="Cloudflare API token" value={created.token} secret visible={visible} onToggle={() => setVisible((v) => !v)} onCopy={copy} />
          {created.credential.expiresOn ? <span className="sz-xs sz-muted">Expires {formatDateTime(created.credential.expiresOn)}.</span> : null}
        </div>
      ) : null}
    </Dialog>
  );
}

function SecretField({ label, value, secret = false, visible = true, onToggle, onCopy }: {
  label: string;
  value: string;
  secret?: boolean;
  visible?: boolean;
  onToggle?: () => void;
  onCopy: (value: string) => Promise<void>;
}) {
  return (
    <div className="sz-row" style={{ alignItems: 'flex-end', flexWrap: 'nowrap' }}>
      <div style={{ flex: 1 }}>
        <Input label={label} readOnly type={secret && !visible ? 'password' : 'text'} value={value} style={{ fontFamily: 'var(--cl-font-mono)' }} />
      </div>
      {secret && onToggle ? <Button variant="ghost" size="sm" onClick={onToggle}>{visible ? 'Hide' : 'Show'}</Button> : null}
      <Button variant={secret ? 'outline' : 'ghost'} size="sm" onClick={() => void onCopy(value)}>Copy{secret ? ' secret' : ''}</Button>
    </div>
  );
}
