import { useEffect, useState, type FormEvent } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import type { IdentityProvider, NewServiceToken, ServiceTokenInfo, TunnelRouteInput } from '../../api/types';
import { ACCESS_MODES, accessFields, emptyAccess, joinLines, splitLines } from '../../app/access';
import { useToast } from '../../app/toast';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Button, Card, Checkbox, Input, Select, Switch, Textarea } from '../../ui';
import { ServiceTokenDialog } from './ServiceTokenDialog';

const blankRoute = (): TunnelRouteInput => ({
  domainName: '',
  forwardScheme: 'http',
  forwardHost: 'localhost',
  forwardPort: 8080,
  isEnabled: true,
  notes: null,
  access: emptyAccess(),
});

export function TunnelRouteEditPage() {
  const { id } = useParams();
  const routeId = id ? Number(id) : null;
  useDocumentTitle(routeId ? 'Edit Tunnel Route' : 'Add Tunnel Route');
  const navigate = useNavigate();
  const notify = useToast();
  const [params] = useSearchParams();

  const [route, setRoute] = useState<TunnelRouteInput | null>(null);
  const [idps, setIdps] = useState<IdentityProvider[]>([]);
  const [tokens, setTokens] = useState<ServiceTokenInfo[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [minted, setMinted] = useState<NewServiceToken | null>(null);

  useEffect(() => {
    let live = true;
    (async () => {
      try {
        let initial: TunnelRouteInput;
        if (routeId) {
          const existing = await api.tunnel.route(routeId);
          initial = { ...existing, access: { ...existing.access } };
        } else {
          // New hostnames start protected: identity mode allowing the owner email.
          initial = { ...blankRoute(), domainName: params.get('domain') ?? '', access: await api.tunnel.defaults() };
        }
        // Both lists come back empty when the account is unreachable, which leaves the form
        // usable with free-text fallbacks rather than blocking on Cloudflare.
        const [idpList, tokenList] = await Promise.all([api.tunnel.identityProviders(), api.tunnel.serviceTokens()]);
        if (!live) return;
        setRoute(initial);
        setIdps(idpList);
        setTokens(tokenList);
      } catch (err) {
        if (!live) return;
        notify((err as Error).message, 'error');
        navigate('/tunnel-routes', { replace: true });
      }
    })();
    return () => { live = false; };
  }, [routeId, params, navigate, notify]);

  if (!route) return <Loading />;

  const set = (patch: Partial<TunnelRouteInput>) => setRoute({ ...route, ...patch });
  const setAccess = (patch: Partial<TunnelRouteInput['access']>) => setRoute({ ...route, access: { ...route.access, ...patch } });
  const fields = accessFields(route.access.mode);
  const selectedIdps = splitLines(route.access.allowedIdpIds);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = routeId ? await api.tunnel.updateRoute(routeId, route) : await api.tunnel.createRoute(route);
      notify(`Route for ${result.route.domainName} saved and pushed to Cloudflare`, 'success');
      // A minted secret exists only in this result: show it before leaving the page.
      if (result.newServiceToken) setMinted(result.newServiceToken);
      else navigate('/tunnel-routes');
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="sz-page sz-page-narrow">
      <PageHeader title={routeId ? 'Edit Tunnel Route' : 'Add Tunnel Route'} />
      <form className="sz-stack" onSubmit={submit} noValidate>
        {error ? <Notice tone="danger" title="The route wasn't saved">{error}</Notice> : null}

        <Card>
          <div className="sz-stack">
            <h2>Public hostname</h2>
            <Input label="Hostname" placeholder="app.yourdomain.com" value={route.domainName} disabled={busy}
              hint="Must be in the zone chosen during tunnel setup. A proxied CNAME is created automatically."
              onChange={(e) => set({ domainName: e.target.value })} />
            <Textarea label="Notes (optional)" rows={2} value={route.notes ?? ''} disabled={busy}
              onChange={(e) => set({ notes: e.target.value })} />
          </div>
        </Card>

        <Card>
          <div className="sz-stack">
            <div className="sz-stack-sm">
              <h2>Local service</h2>
              <p className="sz-small sz-muted">
                Where the connector forwards requests. It's resolved from the machine running <code>cloudflared</code>, so it can be a private address.
              </p>
            </div>
            <Select label="Scheme" value={route.forwardScheme} disabled={busy} onChange={(e) => set({ forwardScheme: e.target.value })}>
              <option value="http">HTTP</option>
              <option value="https">HTTPS</option>
            </Select>
            <Input label="Forward host" hint="IP address, hostname, or Docker container name" placeholder="localhost or 192.168.1.100"
              value={route.forwardHost} disabled={busy} onChange={(e) => set({ forwardHost: e.target.value })} />
            <Input label="Forward port" type="number" min={1} max={65535} value={String(route.forwardPort)} disabled={busy}
              onChange={(e) => set({ forwardPort: Number(e.target.value) })} />
          </div>
        </Card>

        <Card>
          <div className="sz-stack">
            <div className="sz-stack-sm">
              <h2>Cloudflare Access</h2>
              <p className="sz-small sz-muted">Who may reach this hostname. Publishing it without a policy puts the service on the open internet.</p>
            </div>
            <Select label="Protection" value={route.access.mode} disabled={busy}
              onChange={(e) => setAccess({ mode: e.target.value as TunnelRouteInput['access']['mode'] })}>
              {ACCESS_MODES.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
            </Select>

            {route.access.mode === 'none' ? (
              <Notice tone="warning">
                Anyone who knows this hostname will be able to reach the service. Any Access application StageZero created for it will be removed.
              </Notice>
            ) : null}

            {fields.session ? (
              <Input label="Session duration" placeholder="24h" value={route.access.sessionDuration ?? ''} disabled={busy}
                hint="How long a session lasts before signing in again, for example 30m, 24h, 2h45m."
                onChange={(e) => setAccess({ sessionDuration: e.target.value })} />
            ) : null}

            {fields.identity ? (
              <>
                <h3>Who can sign in</h3>
                <Textarea label="Allowed emails" rows={3} mono value={route.access.allowedEmails ?? ''} disabled={busy}
                  hint="One per line. New hostnames start with the owner's email."
                  onChange={(e) => setAccess({ allowedEmails: e.target.value })} />
                <Textarea label="Allowed email domains" rows={2} mono value={route.access.allowedEmailDomains ?? ''} disabled={busy}
                  hint="One per line, for example codelifter.net. Anyone with an address at the domain is let in."
                  onChange={(e) => setAccess({ allowedEmailDomains: e.target.value })} />
                {idps.length > 0 ? (
                  <fieldset className="sz-stack-sm" style={{ border: 'none', padding: 0, margin: 0 }}>
                    <legend className="sz-small sz-muted" style={{ marginBottom: 'var(--cl-space-xs)' }}>
                      Identity providers (leave all unticked to offer every provider on the account)
                    </legend>
                    {idps.map((idp) => (
                      <Checkbox key={idp.id} label={`${idp.name} (${idp.type})`} checked={selectedIdps.includes(idp.id)} disabled={busy}
                        onChange={(e) => setAccess({
                          allowedIdpIds: joinLines(e.target.checked ? [...selectedIdps, idp.id] : selectedIdps.filter((x) => x !== idp.id)),
                        })} />
                    ))}
                  </fieldset>
                ) : null}
              </>
            ) : null}

            {fields.serviceToken ? (
              <>
                <h3>Service token</h3>
                <label className="sz-row">
                  <Switch checked={route.access.createServiceToken} disabled={busy} onChange={(on) => setAccess({ createServiceToken: on })} />
                  <span className="sz-small">Create a new token</span>
                </label>
                <span className="sz-xs sz-muted">
                  The client secret is shown once, when the token is created, and is never stored by StageZero.
                  A token that already exists with the same name is reused rather than duplicated.
                </span>
                {route.access.createServiceToken ? (
                  <>
                    <Input label="Token name" value={route.access.serviceTokenName ?? ''} disabled={busy}
                      onChange={(e) => setAccess({ serviceTokenName: e.target.value })} />
                    <Input label="Token lifetime (optional)" value={route.access.serviceTokenDuration ?? ''} disabled={busy}
                      hint="For example 720h, or forever. Cloudflare defaults to one year."
                      onChange={(e) => setAccess({ serviceTokenDuration: e.target.value })} />
                  </>
                ) : tokens.length > 0 ? (
                  <Select label="Existing token" value={route.access.serviceTokenId ?? ''} disabled={busy}
                    onChange={(e) => setAccess({ serviceTokenId: e.target.value || null })}>
                    <option value="">Choose a token</option>
                    {tokens.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                  </Select>
                ) : (
                  <Input label="Existing token name" value={route.access.serviceTokenName ?? ''} disabled={busy}
                    hint="No tokens found on the account. Turn on 'Create a new token' to mint one."
                    onChange={(e) => setAccess({ serviceTokenName: e.target.value })} />
                )}
              </>
            ) : null}
          </div>
        </Card>

        <Card>
          <div className="sz-stack-sm">
            <h2>Status</h2>
            <label className="sz-row">
              <Switch checked={route.isEnabled} disabled={busy} onChange={(on) => set({ isEnabled: on })} />
              <span className="sz-small">Enabled</span>
            </label>
            <span className="sz-xs sz-muted">Disabling removes the ingress rule but keeps the DNS record, so re-enabling takes effect immediately.</span>
          </div>
        </Card>

        <div className="sz-row">
          <Button type="submit" disabled={busy}>{busy ? 'Saving…' : routeId ? 'Update route' : 'Create route'}</Button>
          <Button type="button" variant="ghost" disabled={busy} onClick={() => navigate('/tunnel-routes')}>Cancel</Button>
        </div>
      </form>
      <ServiceTokenDialog token={minted} hostname={route.domainName.trim().toLowerCase()}
        onDone={() => { setMinted(null); navigate('/tunnel-routes'); }} />
    </div>
  );
}
