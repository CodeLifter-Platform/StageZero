import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import type { CloudflareZone, TunnelConfig, TunnelInfo } from '../../api/types';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, DataTable, Input, Select, Tabs, type DataColumn } from '../../ui';

export type ConnectorOs = 'macos' | 'windows' | 'linux' | 'docker';

/** The install commands for the connector, per host OS, with the token filled in. */
export function connectorInstructions(os: ConnectorOs, token: string): { commands: string; note: string } {
  switch (os) {
    case 'macos':
      return {
        commands: `brew install cloudflared\nsudo cloudflared service install ${token}`,
        note: 'Installs a launchd service. Verify with: sudo launchctl list | grep cloudflared',
      };
    case 'windows':
      return {
        commands: `winget install --id Cloudflare.cloudflared\ncloudflared service install ${token}`,
        note: 'Run in an Administrator terminal. Verify with: Get-Service cloudflared',
      };
    case 'linux':
      return {
        commands: [
          'curl -fsSL https://pkg.cloudflare.com/cloudflare-main.gpg \\',
          '  | sudo tee /usr/share/keyrings/cloudflare-main.gpg >/dev/null',
          'echo "deb [signed-by=/usr/share/keyrings/cloudflare-main.gpg] https://pkg.cloudflare.com/cloudflared any main" \\',
          '  | sudo tee /etc/apt/sources.list.d/cloudflared.list',
          'sudo apt update && sudo apt install cloudflared',
          `sudo cloudflared service install ${token}`,
        ].join('\n'),
        note: 'Installs a systemd unit. Verify with: systemctl status cloudflared',
      };
    case 'docker':
      return {
        commands: `TUNNEL_TOKEN=${token}\ndocker compose -f prod.docker-compose.yml --profile tunnel up -d`,
        note: 'Put the token in .env, then start the stack with the tunnel profile. The connector stops when the stack does; a host service is more resilient.',
      };
  }
}

/** The browser's OS as the first tab; the person can switch, since the server may differ. */
export function guessOs(userAgent: string): ConnectorOs {
  if (/Mac/i.test(userAgent)) return 'macos';
  if (/Win/i.test(userAgent)) return 'windows';
  return 'linux';
}

const OS_TABS = [
  { id: 'macos', label: 'macOS' },
  { id: 'windows', label: 'Windows' },
  { id: 'linux', label: 'Linux' },
  { id: 'docker', label: 'Docker' },
];

export function TunnelSettingsPage() {
  useDocumentTitle('Tunnel Setup');
  const notify = useToast();
  const navigate = useNavigate();
  const [saved, setSaved] = useState<TunnelConfig | null | undefined>(undefined);
  const [apiToken, setApiToken] = useState('');
  const [accountId, setAccountId] = useState('');
  const [zones, setZones] = useState<CloudflareZone[]>([]);
  const [tunnels, setTunnels] = useState<TunnelInfo[]>([]);
  const [zoneId, setZoneId] = useState('');
  const [tunnelName, setTunnelName] = useState('stagezero');
  const [connectorToken, setConnectorToken] = useState<string | null>(null);
  const [wasAdopted, setWasAdopted] = useState(false);
  const [os, setOs] = useState<ConnectorOs>(() => guessOs(navigator.userAgent));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmingDisconnect, setConfirmingDisconnect] = useState(false);

  useEffect(() => {
    api.tunnel.config()
      .then(({ config }) => {
        setSaved(config);
        if (config) {
          setAccountId(config.accountId);
          setZoneId(config.zoneId ?? '');
          setTunnelName(config.tunnelName ?? 'stagezero');
        }
      })
      .catch((err: Error) => { setSaved(null); setError(err.message); });
  }, []);

  const run = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await work(); } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  const connect = () => run(async () => {
    const result = await api.tunnel.loadAccount(apiToken.trim(), accountId.trim());
    setZones(result.zones);
    setTunnels(result.tunnels);
    if (!result.zones.some((z) => z.id === zoneId)) setZoneId(result.zones[0]?.id ?? '');
    notify(`Found ${result.zones.length} zone(s) and ${result.tunnels.length} tunnel(s)`, 'success');
  });

  const create = () => run(async () => {
    const result = await api.tunnel.create(apiToken.trim(), accountId.trim(), zoneId, tunnelName.trim());
    setSaved(result.config);
    setConnectorToken(result.connectorToken);
    setWasAdopted(false);
    notify('Tunnel created', 'success');
  });

  const adopt = (tunnel: TunnelInfo) => run(async () => {
    const result = await api.tunnel.adopt(apiToken.trim(), accountId.trim(), zoneId, tunnel.id);
    setSaved(result.config);
    setConnectorToken(result.connectorToken);
    setWasAdopted(result.wasLocallyManaged);
    notify(`Adopted tunnel ${tunnel.name}`, 'success');
  });

  const showToken = () => run(async () => setConnectorToken((await api.tunnel.connectorToken()).connectorToken));

  const disconnect = () => run(async () => {
    await api.tunnel.clear();
    setSaved(null);
    setConnectorToken(null);
    setZones([]);
    setTunnels([]);
    setConfirmingDisconnect(false);
    notify('Tunnel configuration cleared', 'success');
  });

  if (saved === undefined) return <Loading />;

  const tunnelColumns: DataColumn<TunnelInfo>[] = [
    { key: 'name', label: 'Name', render: (t) => <span>{t.name} <span className="sz-mono sz-muted sz-xs">{t.id}</span></span> },
    {
      key: 'config', label: 'Config', width: 140,
      render: (t) => (t.isRemotelyManaged
        ? <Badge tone="success">Remote</Badge>
        : <Badge tone="warning" title="Switched to remotely-managed config on adopt">Local file</Badge>),
    },
    {
      key: 'adopt', label: '', width: 110,
      render: (t) => <Button variant="outline" size="sm" disabled={busy || !zoneId} onClick={() => void adopt(t)}>Adopt</Button>,
    },
  ];

  const instructions = connectorToken ? connectorInstructions(os, connectorToken) : null;

  return (
    <div className="sz-page sz-page-narrow">
      <PageHeader title="Cloudflare Tunnel Setup">
        StageZero manages your tunnel's routes through the Cloudflare API. Connect an account here, then install the connector on the host.
      </PageHeader>

      {error ? <Notice tone="danger">{error}</Notice> : null}

      {saved ? (
        <Card>
          <div className="sz-stack">
            <h2>Current configuration</h2>
            <dl className="sz-kv">
              <dt>Tunnel</dt><dd>{saved.tunnelName}</dd>
              <dt>Tunnel ID</dt><dd className="sz-mono sz-break">{saved.tunnelId}</dd>
              <dt>Zone</dt><dd>{saved.zoneName}</dd>
              <dt>Account ID</dt><dd className="sz-mono sz-break">{saved.accountId}</dd>
            </dl>
            <div className="sz-row">
              <Button variant="outline" size="sm" disabled={busy} onClick={() => void showToken()}>Show connector token</Button>
              <Button variant="ghost" size="sm" disabled={busy} onClick={() => setConfirmingDisconnect(true)}>Disconnect</Button>
            </div>
          </div>
        </Card>
      ) : null}

      <Card>
        <div className="sz-stack">
          <h2>1. Cloudflare credentials</h2>
          <Notice title="Create an API token with these permissions">
            Account → Cloudflare Tunnel → Edit; Zone → DNS → Edit; Zone → Zone → Read; and for Access,
            Account → Access: Apps → Edit and Account → Access: Service Tokens → Edit.
            The same token also works on the DNS Configuration page.
          </Notice>
          <Input label="API token" type="password" autoComplete="off" value={apiToken} disabled={busy}
            hint="Stored encrypted. Re-enter it to change any setting below." onChange={(e) => setApiToken(e.target.value)} />
          <Input label="Account ID" value={accountId} disabled={busy} className="sz-mono"
            hint="Cloudflare dashboard → Workers & Pages → Account details" onChange={(e) => setAccountId(e.target.value)} />
          <div className="sz-row">
            <Button disabled={busy || !apiToken.trim() || !accountId.trim()} onClick={() => void connect()}>{busy ? 'Working…' : 'Connect'}</Button>
          </div>
        </div>
      </Card>

      {zones.length > 0 ? (
        <Card>
          <div className="sz-stack">
            <h2>2. Zone and tunnel</h2>
            <Select label="Zone" value={zoneId} disabled={busy} onChange={(e) => setZoneId(e.target.value)}>
              {zones.map((z) => <option key={z.id} value={z.id}>{z.name}</option>)}
            </Select>
            <span className="sz-xs sz-muted">Hostnames you publish must belong to this zone.</span>
            {tunnels.length > 0 ? (
              <>
                <h3>Adopt an existing tunnel</h3>
                <Notice tone="warning">
                  Adopting a tunnel that serves traffic will interrupt it. StageZero becomes the source of truth for its routes,
                  so any hostnames it already serves must be re-created here before they work again.
                </Notice>
                <DataTable caption="Tunnels on the account" density="standard" columns={tunnelColumns} rows={tunnels} rowKey={(t) => t.id} />
              </>
            ) : null}
            <h3>Or create a new one</h3>
            <Input label="Tunnel name" placeholder="stagezero" value={tunnelName} disabled={busy} onChange={(e) => setTunnelName(e.target.value)} />
            <div className="sz-row">
              <Button disabled={busy || !tunnelName.trim() || !zoneId} onClick={() => void create()}>Create tunnel</Button>
            </div>
          </div>
        </Card>
      ) : null}

      {instructions ? (
        <Card>
          <div className="sz-stack">
            <h2>3. Install the connector</h2>
            <Notice tone="warning">Treat this token like a password: anyone holding it can serve traffic through your tunnel.</Notice>
            <pre className="sz-code" aria-label="Connector token">{connectorToken}</pre>
            <p className="sz-small">Run these on the machine that should serve the traffic:</p>
            <Tabs tabs={OS_TABS} value={os} onChange={(id) => setOs(id as ConnectorOs)} />
            <pre className="sz-code">{instructions.commands}</pre>
            <span className="sz-xs sz-muted">{instructions.note}</span>
            {wasAdopted ? (
              <Notice>
                This tunnel previously used a local <code>config.yml</code>. After installing with the token above, run{' '}
                <code>cloudflared service uninstall</code> for the old service first, then delete the old <code>config.yml</code> so it can't take precedence.
              </Notice>
            ) : null}
            <div className="sz-row"><Button onClick={() => navigate('/tunnel-routes')}>Go to tunnel routes</Button></div>
          </div>
        </Card>
      ) : null}

      <ConfirmDialog open={confirmingDisconnect} busy={busy} title="Disconnect the tunnel" confirmLabel="Disconnect"
        message="Forget the saved Cloudflare account and tunnel? The tunnel and its routes stay on Cloudflare; StageZero stops managing them until you connect again."
        onCancel={() => setConfirmingDisconnect(false)} onConfirm={() => void disconnect()} />
    </div>
  );
}
