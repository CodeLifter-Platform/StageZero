import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { DnsProvider, DnsRecord } from '../../api/types';
import { groupRecords, importSummary, type GroupedRecord } from '../../app/dns';
import { formatDateTime } from '../../app/format';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, DataTable, Switch, type DataColumn } from '../../ui';
import { AddProviderDialog, AddRecordDialog, EditRecordDialog } from './RecordDialogs';

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
  const [addingProvider, setAddingProvider] = useState(false);
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

  const act = async (key: string, work: () => Promise<void>) => {
    setBusy(key);
    try { await work(); } catch (err) { notify((err as Error).message, 'error'); } finally { setBusy(null); }
  };

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

  if (!providers && !loadError) return <Loading />;

  return (
    <div className="sz-page">
      <PageHeader title="DNS Configuration" actions={<>
        <label className="sz-row">
          <Switch checked={showAllCnames} onChange={setShowAllCnames} />
          <span className="sz-small">Show all CNAMEs</span>
        </label>
        <Button onClick={() => setAddingProvider(true)}>Add Cloudflare provider</Button>
      </>}>
        Each provider is a Cloudflare zone. Its A records follow your public IP; AAAA and CNAME records are tracked but left alone.
      </PageHeader>

      {loadError ? <Notice tone="danger" title="Couldn't load DNS providers">{loadError}</Notice> : null}

      {providers && providers.length === 0 ? (
        <Notice title="No DNS providers configured">Add a Cloudflare provider to start managing your DNS records automatically.</Notice>
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

      <AddProviderDialog open={addingProvider} onClose={() => setAddingProvider(false)}
        onAdded={(p) => { setAddingProvider(false); notify(`Provider '${p.name}' added`, 'success'); void load(); }} />
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
