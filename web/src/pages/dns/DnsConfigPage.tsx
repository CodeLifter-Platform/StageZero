import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { CloudflareZone, DnsProvider, DnsRecord } from '../../api/types';
import { groupRecords, importSummary, type GroupedRecord } from '../../app/dns';
import { formatDateTime } from '../../app/format';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, DataTable, Switch, type DataColumn } from '../../ui';
import { AddRecordDialog, EditRecordDialog } from './RecordDialogs';

type Pending =
  | { kind: 'provider'; provider: DnsProvider }
  | { kind: 'record'; record: DnsRecord };

const TYPE_TONE = { A: 'primary', AAAA: 'accent', CNAME: 'muted' } as const;

export function DnsConfigPage() {
  useDocumentTitle('DNS Configuration');
  const notify = useToast();
  const navigate = useNavigate();
  const [providers, setProviders] = useState<DnsProvider[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [showAllCnames, setShowAllCnames] = useState(false);
  const [hasToken, setHasToken] = useState<boolean | null>(null);
  const [zones, setZones] = useState<CloudflareZone[] | null>(null);
  const [addingRecordTo, setAddingRecordTo] = useState<DnsProvider | null>(null);
  const [editing, setEditing] = useState<DnsRecord | null>(null);
  const [pending, setPending] = useState<Pending | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setProviders(await api.dns.providers());
      setLoadError(null);
    } catch (err) {
      setLoadError((err as Error).message);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    api.settings.cloudflare()
      .then((c) => setHasToken(c.status.isConfigured && c.status.isReadable))
      .catch(() => setHasToken(null));
  }, []);

  const act = async (key: string, work: () => Promise<void>) => {
    setBusy(key);
    try { await work(); } catch (err) { notify((err as Error).message, 'error'); } finally { setBusy(null); }
  };

  const loadZones = () => act('zones', async () => {
    const found = await api.dns.zones();
    setZones(found);
    notify(`Found ${found.length} zone(s)`, found.length > 0 ? 'success' : 'info');
  });

  const addProvider = (zone: CloudflareZone) => act(`add-${zone.id}`, async () => {
    const p = await api.dns.addProvider(zone.name, zone.id, true);
    notify(`Provider '${p.name}' added`, 'success');
    await load();
  });

  const toggle = (p: DnsProvider) => act(`toggle-${p.id}`, async () => {
    const updated = await api.dns.toggleProvider(p.id);
    notify(`DNS updates ${updated.isActive ? 'enabled' : 'disabled'} for '${p.name}'`, updated.isActive ? 'success' : 'info');
    await load();
  });

  const importAll = (p: DnsProvider) => act(`import-${p.id}`, async () => {
    const result = await api.dns.importAll(p.id);
    const summary = importSummary(result.imported, result.skipped);
    notify(summary.message, summary.tone);
    await load();
  });

  const confirmDelete = () => {
    if (!pending) return;
    void act('delete', async () => {
      if (pending.kind === 'provider') {
        await api.dns.deleteProvider(pending.provider.id);
        notify('Provider deleted', 'success');
      } else {
        await api.dns.deleteRecord(pending.record.id);
        notify('DNS record deleted', 'success');
      }
      setPending(null);
      await load();
    });
  };

  // Jump to the existing route for this hostname, or open the add form with it filled in.
  const configureRoute = (name: string) => act(`route-${name}`, async () => {
    const { routes } = await api.tunnel.routes();
    const existing = routes.find((r) => r.domainName.toLowerCase() === name.toLowerCase());
    navigate(existing ? `/tunnel-routes/edit/${existing.id}` : `/tunnel-routes/add?domain=${encodeURIComponent(name)}`);
  });

  const columns: DataColumn<GroupedRecord>[] = [
    {
      key: 'name', label: 'Record name',
      render: ({ record, nested }) => (
        <span title={record.recordName} className={nested ? 'sz-indent' : undefined}>
          {nested ? <span aria-label="points at the record above">{'↳ '}</span> : null}{record.recordName}
        </span>
      ),
    },
    { key: 'type', label: 'Type', width: 90, render: ({ record }) => <Badge tone={TYPE_TONE[record.recordType as keyof typeof TYPE_TONE] ?? 'muted'}>{record.recordType}</Badge> },
    {
      key: 'value', label: 'Content / last IP', mono: true,
      render: ({ record }) => (record.recordType === 'CNAME' ? record.content : record.lastIpAddress) ?? <span className="sz-muted">-</span>,
    },
    { key: 'updated', label: 'Last updated', muted: true, render: ({ record }) => (record.lastUpdatedAt ? formatDateTime(record.lastUpdatedAt) : 'Never') },
    {
      key: 'auto', label: 'Auto update', width: 120,
      render: ({ record }) => (record.autoUpdate ? <Badge tone="success">Enabled</Badge> : <Badge tone="muted" title={record.autoUpdateUnavailableReason ?? undefined}>Off</Badge>),
    },
    {
      key: 'actions', label: 'Actions', width: 260,
      render: ({ record }) => (
        <div className="sz-row" style={{ flexWrap: 'nowrap' }}>
          <Button variant="ghost" size="sm" onClick={() => setEditing(record)}>Edit</Button>
          {record.recordType === 'CNAME'
            ? <Button variant="ghost" size="sm" disabled={busy === `route-${record.recordName}`} onClick={() => void configureRoute(record.recordName)}>Tunnel route</Button>
            : null}
          <Button variant="ghost" size="sm" onClick={() => setPending({ kind: 'record', record })}>Delete</Button>
        </div>
      ),
    },
  ];

  const zoneColumns: DataColumn<CloudflareZone>[] = [
    { key: 'name', label: 'Zone', render: (z) => <span>{z.name} <span className="sz-mono sz-muted sz-xs">{z.id}</span></span> },
    {
      key: 'add', label: '', width: 140,
      render: (z) => (providers?.some((p) => p.zoneId === z.id)
        ? <Badge tone="success">Added</Badge>
        : <Button variant="outline" size="sm" disabled={busy !== null} onClick={() => void addProvider(z)}>
            {busy === `add-${z.id}` ? 'Adding…' : 'Add provider'}
          </Button>),
    },
  ];

  if (!providers && !loadError) return <Loading />;

  return (
    <div className="sz-page">
      <PageHeader title="DNS Configuration" actions={<>
        <label className="sz-row">
          <Switch checked={showAllCnames} onChange={setShowAllCnames} />
          <span className="sz-small">Show all CNAMEs</span>
        </label>
      </>}>
        Each provider is a Cloudflare zone. Its A records follow your public IP; AAAA and CNAME records are tracked but left alone.
      </PageHeader>

      {loadError ? <Notice tone="danger" title="Couldn't load DNS providers">{loadError}</Notice> : null}

      {providers && providers.length === 0 ? (
        <Notice title="No DNS providers configured">Add a zone below to start managing its DNS records automatically.</Notice>
      ) : null}

      {providers?.map((p) => (
        <Card key={p.id}>
          <div className="sz-card-header">
            <div className="sz-stack-sm">
              <h2>{p.name}</h2>
              <span className="sz-small sz-muted">{p.providerType} · zone <span className="sz-mono">{p.zoneId}</span></span>
              <label className="sz-row">
                <Switch checked={p.isActive} disabled={busy === `toggle-${p.id}`} onChange={() => void toggle(p)} />
                <span className="sz-small">{p.isActive ? 'Updates enabled' : 'Updates disabled'}</span>
              </label>
            </div>
            <div className="sz-row">
              <Button variant="outline" size="sm" disabled={busy === `import-${p.id}`} onClick={() => void importAll(p)}>
                {busy === `import-${p.id}` ? 'Importing…' : 'Import all records'}
              </Button>
              <Button variant="outline" size="sm" onClick={() => setAddingRecordTo(p)}>Add DNS record</Button>
              <Button variant="ghost" size="sm" onClick={() => setPending({ kind: 'provider', provider: p })}>Delete provider</Button>
            </div>
          </div>
          <DataTable caption={`DNS records for ${p.name}`} density="standard" columns={columns}
            rows={groupRecords(p.records, showAllCnames)} rowKey={(r) => r.record.id} empty="No DNS records configured" />
        </Card>
      ))}

      <Card>
        <div className="sz-stack">
          <h2>Add a zone</h2>
          {hasToken === false ? (
            <Notice tone="warning" title="No Cloudflare API token saved"
              action={<Button size="sm" onClick={() => navigate('/settings')}>Open Settings</Button>}>
              StageZero reaches Cloudflare with the one token saved in Settings (your email, top right). Add it there, then come back to pick a zone.
            </Notice>
          ) : (
            <>
              <p className="sz-small sz-muted">
                Zones come from the Cloudflare API token saved in <a href="/settings" onClick={(e) => { e.preventDefault(); navigate('/settings'); }}>Settings</a>.
              </p>
              <div className="sz-row">
                <Button disabled={busy !== null} onClick={() => void loadZones()}>{busy === 'zones' ? 'Loading…' : 'Load zones'}</Button>
              </div>
            </>
          )}
          {zones ? (
            <DataTable caption="Zones this token can manage" density="standard" columns={zoneColumns}
              rows={zones} rowKey={(z) => z.id} empty="No zones found for this API token" />
          ) : null}
        </div>
      </Card>

      <AddRecordDialog provider={addingRecordTo} onClose={() => setAddingRecordTo(null)}
        onSaved={(r) => { setAddingRecordTo(null); notify(`DNS record '${r.recordName}' added`, 'success'); void load(); }} />
      <EditRecordDialog record={editing} onClose={() => setEditing(null)}
        onSaved={() => { setEditing(null); notify('DNS record updated', 'success'); void load(); }} />
      <ConfirmDialog
        open={pending !== null}
        busy={busy === 'delete'}
        title={pending?.kind === 'provider' ? 'Delete provider' : 'Delete DNS record'}
        message={pending?.kind === 'provider'
          ? `Delete the provider '${pending.provider.name}'? This also deletes all of its DNS records from StageZero (Cloudflare keeps them).`
          : pending ? `Delete the DNS record '${pending.record.recordName}' (${pending.record.recordType}) from StageZero? Cloudflare keeps it.` : ''}
        onCancel={() => setPending(null)}
        onConfirm={confirmDelete}
      />
    </div>
  );
}
