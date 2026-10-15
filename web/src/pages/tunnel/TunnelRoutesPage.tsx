import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { NewServiceToken, RoutesResult, TunnelRoute } from '../../api/types';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, DataTable, StatusDot, type DataColumn } from '../../ui';
import { ServiceTokenDialog } from './ServiceTokenDialog';

export function TunnelRoutesPage() {
  useDocumentTitle('Tunnel Routes');
  const notify = useToast();
  const navigate = useNavigate();
  const [data, setData] = useState<RoutesResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<TunnelRoute | null>(null);
  const [busy, setBusy] = useState<number | null>(null);
  const [minted, setMinted] = useState<{ token: NewServiceToken; hostname: string } | null>(null);

  const load = useCallback(async () => {
    try {
      setData(await api.tunnel.routes());
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const toggle = async (route: TunnelRoute) => {
    setBusy(route.id);
    try {
      const result = await api.tunnel.toggleRoute(route.id);
      notify(`${route.domainName} ${result.route.isEnabled ? 'enabled' : 'disabled'}`, 'success');
      // Re-enabling can mint a service token that was still pending; its secret exists only here.
      if (result.newServiceToken) setMinted({ token: result.newServiceToken, hostname: route.domainName });
      await load();
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const remove = async () => {
    if (!deleting) return;
    setBusy(deleting.id);
    try {
      const result = await api.tunnel.deleteRoute(deleting.id);
      notify('Route deleted', 'success');
      // Say what was left behind rather than letting a retained token go unnoticed.
      if (result.serviceTokenRetainedReason) notify(result.serviceTokenRetainedReason, 'info');
      result.warnings.forEach((w) => notify(w, 'warning'));
      setDeleting(null);
      await load();
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setBusy(null);
    }
  };

  const columns: DataColumn<TunnelRoute>[] = [
    {
      key: 'domainName', label: 'Hostname', sortable: true,
      render: (r) => <span title={r.notes ?? undefined}><span className="sz-strong">{r.domainName}</span>{r.notes ? <span className="sz-muted"> · {r.notes}</span> : null}</span>,
    },
    { key: 'forwardUrl', label: 'Forwards to', mono: true },
    {
      key: 'access', label: 'Access', width: 200,
      render: (r) => (r.access.mode === 'none'
        ? <Badge tone="danger" title="No Access policy: anyone can reach this hostname">Public</Badge>
        : <Badge tone="success">{r.access.modeName ?? r.access.mode}</Badge>),
    },
    {
      key: 'status', label: 'Status', width: 110,
      render: (r) => <StatusDot tone={r.isEnabled ? 'success' : 'idle'} label={r.isEnabled ? 'Active' : 'Disabled'} />,
    },
    {
      key: 'actions', label: 'Actions', width: 330,
      render: (r) => (
        <div className="sz-row" style={{ flexWrap: 'nowrap' }}>
          <Button variant="ghost" size="sm" onClick={() => navigate(`/tunnel-routes/access/${r.id}`)}>Access</Button>
          <Button variant="ghost" size="sm" onClick={() => navigate(`/tunnel-routes/edit/${r.id}`)}>Edit</Button>
          <Button variant="ghost" size="sm" disabled={busy === r.id} onClick={() => void toggle(r)}>{r.isEnabled ? 'Disable' : 'Enable'}</Button>
          <Button variant="ghost" size="sm" disabled={busy === r.id} onClick={() => setDeleting(r)}>Delete</Button>
        </div>
      ),
    },
  ];

  if (!data && !error) return <Loading />;

  return (
    <div className="sz-page">
      <PageHeader title="Tunnel Routes" actions={<>
        <Button variant="ghost" onClick={() => navigate('/tunnel-settings')}>Tunnel setup</Button>
        <Button onClick={() => navigate('/tunnel-routes/add')}>Add route</Button>
      </>}>
        Each route publishes a hostname through your Cloudflare Tunnel and forwards it to a service on this network.
        Cloudflare handles TLS, so there are no ports to open.
      </PageHeader>

      {error ? <Notice tone="danger" title="Couldn't load the routes">{error}</Notice> : null}

      {data && !data.isConfigured ? (
        <Notice tone="warning" title="Cloudflare Tunnel isn't set up yet"
          action={<Button size="sm" onClick={() => navigate('/tunnel-settings')}>Go to tunnel setup</Button>}>
          Routes saved here can't reach Cloudflare until setup is complete.
        </Notice>
      ) : data ? (
        <Notice tone="success">
          Connected to tunnel <strong>{data.tunnelName}</strong>{data.zoneName ? <> on zone <strong>{data.zoneName}</strong></> : null}
        </Notice>
      ) : null}

      {data ? (
        <Card>
          <DataTable caption="Tunnel routes" density="standard" columns={columns} rows={data.routes}
            empty='No tunnel routes yet. Use "Add route" to publish your first hostname.' />
        </Card>
      ) : null}

      <ConfirmDialog
        open={deleting !== null}
        busy={deleting !== null && busy === deleting.id}
        title="Delete tunnel route"
        message={deleting ? `Delete the route for '${deleting.domainName}'? This removes the tunnel ingress rule, the DNS record, and its Cloudflare Access application and policies.` : ''}
        onCancel={() => setDeleting(null)}
        onConfirm={() => void remove()}
      />
      <ServiceTokenDialog token={minted?.token ?? null} hostname={minted?.hostname ?? ''} onDone={() => setMinted(null)} />
    </div>
  );
}
