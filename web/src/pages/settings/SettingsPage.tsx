import { useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { CloudflareCredential, CloudflarePermission, CloudflarePermissionCheck } from '../../api/types';
import { formatDateTime } from '../../app/format';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, DataTable, Input, type DataColumn } from '../../ui';

const CLOUDFLARE_TOKENS_URL = 'https://dash.cloudflare.com/profile/api-tokens';

interface PermissionRow { permission: CloudflarePermission; check: CloudflarePermissionCheck | undefined }

/** The required permissions, each with what the last save or check found for it. */
export function permissionRows(credential: CloudflareCredential): PermissionRow[] {
  return credential.required.map((permission) => ({
    permission,
    check: credential.checks?.find((c) => c.permission.scope === permission.scope && c.permission.item === permission.item),
  }));
}

/** The account settings behind the email in the top bar: the one Cloudflare token StageZero uses. */
export function SettingsPage() {
  useDocumentTitle('Settings');
  const notify = useToast();
  const [credential, setCredential] = useState<CloudflareCredential | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [apiToken, setApiToken] = useState('');
  const [accountId, setAccountId] = useState('');
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmingRemove, setConfirmingRemove] = useState(false);

  useEffect(() => {
    api.settings.cloudflare()
      .then((c) => { setCredential(c); setEditing(!c.status.isConfigured); })
      .catch((err: Error) => setLoadError(err.message));
  }, []);

  const run = async (key: string, work: () => Promise<void>) => {
    setBusy(key);
    setError(null);
    try { await work(); } catch (err) { setError((err as Error).message); } finally { setBusy(null); }
  };

  const save = () => run('save', async () => {
    const replacing = credential?.status.isConfigured ?? false;
    const saved = await api.settings.saveCloudflare(apiToken.trim(), accountId.trim());
    setCredential(saved);
    setApiToken('');
    setEditing(false);
    const missing = saved.checks?.filter((c) => !c.granted).length ?? 0;
    notify(
      missing === 0 ? `Cloudflare token ${replacing ? 'replaced' : 'saved'}` : `Token saved; ${missing} permission(s) missing`,
      missing === 0 ? 'success' : 'info',
    );
  });

  const check = () => run('check', async () => {
    const checked = await api.settings.checkCloudflare();
    setCredential(checked);
    const missing = checked.checks?.filter((c) => !c.granted).length ?? 0;
    notify(missing === 0 ? 'The token has every permission' : `${missing} permission(s) missing`, missing === 0 ? 'success' : 'info');
  });

  const remove = () => run('remove', async () => {
    await api.settings.removeCloudflare();
    setCredential((c) => (c ? { ...c, checks: null, status: { ...c.status, isConfigured: false, isReadable: false } } : c));
    setConfirmingRemove(false);
    setEditing(true);
    notify('Cloudflare token removed from StageZero', 'success');
  });

  if (!credential && !loadError) return <Loading />;

  const status = credential?.status;
  const checked = credential?.checks != null;

  // DataTable rows are one line, so the table carries the short columns and the list below
  // it the sentences: what each permission is for, and what the check saw.
  const columns: DataColumn<PermissionRow>[] = [
    { key: 'scope', label: 'Scope', width: 110, render: (r) => r.permission.scope },
    { key: 'item', label: 'Item', render: (r) => <span className="sz-strong">{r.permission.item}</span> },
    { key: 'level', label: 'Level', width: 90, render: (r) => r.permission.level },
    ...(checked ? [{
      key: 'check', label: 'This token', width: 130,
      render: (r: PermissionRow) => (r.check
        ? (r.check.granted ? <Badge tone="success" title={r.check.detail}>Granted</Badge> : <Badge tone="danger" title={r.check.detail}>Missing</Badge>)
        : null),
    }] : []),
  ];

  return (
    <div className="sz-page sz-page-narrow">
      <PageHeader title="Settings">Your StageZero account's connection to Cloudflare.</PageHeader>

      {loadError ? <Notice tone="danger" title="Couldn't load settings">{loadError}</Notice> : null}

      {credential ? (
        <Card>
          <div className="sz-stack">
            <h2>Cloudflare API token</h2>
            <p className="sz-small">
              StageZero uses <strong>one</strong> Cloudflare API token for everything: keeping your DNS records on your public IP
              (DNS Configuration), creating and routing your tunnel (Tunnel Setup and Tunnel Routes), and putting Cloudflare Access in
              front of those routes. Save it once here; every page uses it. It is stored encrypted and never shown again.
            </p>

            <h3>Permissions the token needs</h3>
            <DataTable caption="Permissions the Cloudflare token needs" density="standard" columns={columns}
              rows={permissionRows(credential)} rowKey={(r) => `${r.permission.scope}-${r.permission.item}`} />
            <dl className="sz-kv" aria-label="What each permission is for">
              {permissionRows(credential).map(({ permission, check: result }) => (
                <div key={`${permission.scope}-${permission.item}`} style={{ display: 'contents' }}>
                  <dt>{permission.item}</dt>
                  <dd>
                    {permission.usedFor}
                    {result ? <span className="sz-xs sz-muted">{' — '}{result.granted ? '' : 'missing: '}{result.detail}</span> : null}
                  </dd>
                </div>
              ))}
            </dl>
            <p className="sz-xs sz-muted">
              A token with only the two Zone rows still runs DNS updates; the tunnel and Access need the Account rows too.
              Optional: Account → Access: Organizations, Identity Providers, and Groups → Read fills the identity-provider picker
              in the route editor.
              Cloudflare can't report Edit rights without making a change, so DNS shows Granted when records can be read; the
              first update proves the rest.
            </p>

            <Notice title="Creating the token in Cloudflare">
              <ol className="sz-list">
                <li>Open <a href={CLOUDFLARE_TOKENS_URL} target="_blank" rel="noreferrer">My Profile → API Tokens</a>, then
                  Create Token → Custom token → Get started.</li>
                <li>Add the five permissions above (+ Add more for each row).</li>
                <li>Account Resources: Include → your account. Zone Resources: Include → All zones, or the zones StageZero should manage.</li>
                <li>Token expiration: No expiration or 1 year. Don't give it a custom start date in the future — the token won't work until then.</li>
                <li>Leave Client IP address filtering empty: your public IP changes, which is why StageZero exists.</li>
                <li>Continue to summary → Create Token, and copy the token (Cloudflare shows it once).</li>
              </ol>
            </Notice>

            {error ? <Notice tone="danger">{error}</Notice> : null}

            {status?.isConfigured ? (
              <>
                <h3>Saved token</h3>
                {!status.isReadable ? (
                  <Notice tone="warning" title="The saved token can't be decrypted">
                    The data-protection keys it was saved with are gone (a lost <code>/app-data</code> volume). Replace it below.
                  </Notice>
                ) : null}
                <dl className="sz-kv">
                  <dt>Account</dt>
                  <dd>{status.accountId
                    ? <>{status.accountName ?? ''} <span className="sz-mono sz-muted sz-break">{status.accountId}</span></>
                    : <span className="sz-muted">None: the token has no account permissions</span>}</dd>
                  <dt>Token ID</dt><dd className="sz-mono sz-break">{status.tokenId ?? '-'}</dd>
                  <dt>Expires</dt><dd>{status.expiresOn ? formatDateTime(status.expiresOn) : 'Never'}</dd>
                  <dt>Last verified</dt><dd>{status.verifiedAt ? formatDateTime(status.verifiedAt) : 'Not yet'}</dd>
                </dl>
                <div className="sz-row">
                  <Button variant="outline" size="sm" disabled={busy !== null || !status.isReadable} onClick={() => void check()}>
                    {busy === 'check' ? 'Checking…' : 'Check permissions'}
                  </Button>
                  {!editing ? <Button variant="outline" size="sm" disabled={busy !== null} onClick={() => setEditing(true)}>Replace token</Button> : null}
                  <Button variant="ghost" size="sm" disabled={busy !== null} onClick={() => setConfirmingRemove(true)}>Remove</Button>
                </div>
              </>
            ) : null}

            {editing ? (
              <>
                <h3>{status?.isConfigured ? 'Replace the token' : 'Add a token'}</h3>
                <Input label="API token" type="password" autoComplete="off" value={apiToken} disabled={busy !== null}
                  hint="Checked with Cloudflare before it is saved. Stored encrypted; never shown again."
                  onChange={(e) => setApiToken(e.target.value)} />
                <Input label="Account ID (optional)" value={accountId} disabled={busy !== null} className="sz-mono"
                  hint="Only needed when the token can reach more than one account. It's the long hex string in your dashboard URL."
                  onChange={(e) => setAccountId(e.target.value)} />
                <div className="sz-row">
                  <Button disabled={busy !== null || !apiToken.trim()} onClick={() => void save()}>
                    {busy === 'save' ? 'Checking with Cloudflare…' : status?.isConfigured ? 'Replace token' : 'Save token'}
                  </Button>
                  {status?.isConfigured ? (
                    <Button variant="ghost" disabled={busy !== null} onClick={() => { setEditing(false); setApiToken(''); setError(null); }}>Cancel</Button>
                  ) : null}
                </div>
              </>
            ) : null}
          </div>
        </Card>
      ) : null}

      <ConfirmDialog open={confirmingRemove} busy={busy === 'remove'} title="Remove the Cloudflare token" confirmLabel="Remove"
        message={'Remove the token from StageZero? DNS updates, tunnel route changes and Access stop until you add one again; '
          + 'what is already on Cloudflare stays as it is. The token itself keeps working until you delete it in Cloudflare '
          + '(My Profile → API Tokens), so do that too if you are revoking it.'}
        onCancel={() => setConfirmingRemove(false)} onConfirm={() => void remove()} />
    </div>
  );
}
