import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../../api/client';
import type { AccessStatus } from '../../api/types';
import { MODE_NAMES } from '../../app/access';
import { formatDateTime } from '../../app/format';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card } from '../../ui';

const DECISION_TONE: Record<string, 'success' | 'accent' | 'warning' | 'danger' | 'muted'> = {
  allow: 'success', non_identity: 'accent', bypass: 'warning', deny: 'danger',
};

/** What Cloudflare actually has for a hostname, read live so drift shows up. */
export function AccessStatusPage() {
  useDocumentTitle('Access Status');
  const { id } = useParams();
  const routeId = Number(id);
  const navigate = useNavigate();
  const [hostname, setHostname] = useState('');
  const [status, setStatus] = useState<AccessStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const route = await api.tunnel.route(routeId);
      setHostname(route.domainName);
      setStatus(await api.tunnel.accessStatus(routeId));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }, [routeId]);

  useEffect(() => { void load(); }, [load]);

  if (loading && !status) return <Loading label="Reading Access configuration…" />;

  const app = status?.application;
  const token = status?.serviceToken;

  return (
    <div className="sz-page sz-page-narrow">
      <PageHeader title="Access Status" actions={<>
        <Button variant="ghost" onClick={() => navigate('/tunnel-routes')}>Back to routes</Button>
        <Button variant="ghost" onClick={() => void load()} disabled={loading}>Refresh</Button>
        <Button onClick={() => navigate(`/tunnel-routes/edit/${routeId}`)}>Edit route</Button>
      </>}>
        Live from Cloudflare{hostname ? <> for <strong>{hostname}</strong></> : null}.
      </PageHeader>

      {error ? <Notice tone="danger">{error}</Notice> : null}
      {status?.warnings.map((w) => <Notice key={w} tone="warning">{w}</Notice>)}

      {status ? (
        <>
          <Card>
            <div className="sz-stack">
              <h2>Application</h2>
              {!app ? (
                <Notice tone="danger">No Access application covers this hostname. It is reachable by anyone who knows the address.</Notice>
              ) : (
                <dl className="sz-kv">
                  <dt>Configured mode</dt><dd>{MODE_NAMES[status.configuredMode]}</dd>
                  <dt>Name</dt><dd>{app.name}</dd>
                  <dt>Domain</dt><dd className="sz-mono">{app.domain}</dd>
                  <dt>Session duration</dt><dd>{app.sessionDuration ?? 'Cloudflare default'}</dd>
                  <dt>Audience (aud)</dt><dd className="sz-mono sz-break">{app.aud ?? '-'}</dd>
                  <dt>Identity providers</dt><dd>{app.allowedIdpIds.length === 0 ? 'All providers on the account' : app.allowedIdpIds.join(', ')}</dd>
                </dl>
              )}
            </div>
          </Card>

          <Card>
            <div className="sz-stack">
              <div className="sz-stack-sm">
                <h2>Policies</h2>
                <span className="sz-small sz-muted">Account-level reusable policies, in the order Access evaluates them.</span>
              </div>
              {status.policies.length === 0 ? <span className="sz-small sz-muted">No policies are attached.</span> : status.policies.map((p) => (
                <div key={p.id} className="sz-stack-sm" style={{ border: '1px solid var(--cl-border)', borderRadius: 'var(--cl-radius-md)', padding: 'var(--cl-space-sm) var(--cl-space-md)' }}>
                  <div className="sz-row"><span className="sz-strong">{p.name}</span><Badge tone={DECISION_TONE[p.decision] ?? 'muted'}>{p.decision}</Badge></div>
                  {p.includeSummary.map((rule) => <span key={rule} className="sz-mono sz-small">{rule}</span>)}
                </div>
              ))}
            </div>
          </Card>

          <Card>
            <div className="sz-stack">
              <h2>Service token</h2>
              {!token ? <span className="sz-small sz-muted">No service token is attached to this hostname.</span> : (
                <>
                  <dl className="sz-kv">
                    <dt>Name</dt><dd>{token.name}</dd>
                    <dt>Client ID</dt><dd className="sz-mono sz-break">{token.clientId ?? '-'}</dd>
                    <dt>Expires</dt><dd>{token.expiresAt ? formatDateTime(token.expiresAt) : 'Does not expire'}</dd>
                    <dt>Created by</dt><dd>{status.serviceTokenCreatedByStageZero ? 'StageZero' : 'Someone else'}</dd>
                    <dt>Used by</dt><dd>{status.serviceTokenRouteCount} route{status.serviceTokenRouteCount === 1 ? '' : 's'}</dd>
                  </dl>
                  <span className="sz-xs sz-muted">
                    The client secret is shown only when a token is created and is never stored by StageZero. If it has been lost, rotate the token in the Cloudflare dashboard.
                  </span>
                  {!status.serviceTokenCreatedByStageZero
                    ? <Notice>StageZero didn't create this token, so deleting the route will leave it in place.</Notice>
                    : status.serviceTokenRouteCount > 1
                      ? <Notice>Other routes use this token, so deleting this route will leave it in place.</Notice>
                      : null}
                </>
              )}
            </div>
          </Card>
        </>
      ) : null}
    </div>
  );
}
