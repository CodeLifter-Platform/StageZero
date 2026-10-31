import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type {
  ConnectionTest, CreatedPostgres, DatabaseProvider, DatabaseStatus, PostgresDiscovery,
} from '../../api/types';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, Checkbox, Input, Segmented } from '../../ui';

export const PROVIDER_LABEL: Record<DatabaseProvider, string> = { sqlite: 'SQLite', postgres: 'PostgreSQL' };

/** Who decided on this database, in words the operator can act on. */
export function sourceExplanation(status: DatabaseStatus): string {
  switch (status.source) {
    case 'environment':
      return 'Set by STAGEZERO_DB_PROVIDER and STAGEZERO_DB_CONNECTION in the environment (.env or the compose file). '
        + 'Change those and restart StageZero; this page can only show it.';
    case 'settings':
      return 'Chosen on this page. The connection string is kept encrypted in database.json in the data directory; '
        + 'delete that file to go back to SQLite by hand.';
    default:
      return 'The default: one file in the data directory, nothing to set up.';
  }
}

/** What a successful test found, and whether it suits the switch being asked for. */
export function testSummary(test: ConnectionTest, copyData: boolean): { tone: 'success' | 'warning'; text: string } {
  if (!test.databaseExists) {
    return { tone: 'success', text: 'The database does not exist yet; StageZero will create it (the account needs the CREATEDB right).' };
  }
  if (test.hasStageZeroSchema) {
    return copyData
      ? { tone: 'warning', text: 'This database already holds a StageZero schema. Copying needs an empty database; to use it as it is, untick the copy.' }
      : { tone: 'success', text: 'This database already holds a StageZero schema; StageZero will pick it up as it is.' };
  }
  if (!test.isEmpty) {
    return copyData
      ? { tone: 'warning', text: 'This database already has tables. Copying needs an empty database.' }
      : { tone: 'warning', text: 'This database already has tables; StageZero will add its own beside them.' };
  }
  return { tone: 'success', text: 'The database is empty and ready.' };
}

/** The connection string for StageZero's own role on a server; the password is the operator's to fill. */
export function connectionStringFor(host: string, port: number, password = ''): string {
  return `Host=${host};Port=${port};Database=stagezero;Username=stagezero;Password=${password}`;
}

/** Letters, digits and a few symbols that survive a connection string, a shell and a SQL literal. */
const PASSWORD_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_.!*+';

export function generatePassword(length = 24): string {
  const bytes = new Uint8Array(length);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => PASSWORD_ALPHABET[b % PASSWORD_ALPHABET.length]).join('');
}

export const MIN_PASSWORD_LENGTH = 12;

/** Why the Create button is off, mirroring the server's rules so the first refusal is not a round trip. */
export function createProblem(form: { port: string; adminPassword: string; appPassword: string; recorded: boolean }): string | null {
  const port = Number(form.port);
  if (!Number.isInteger(port) || port < 1024 || port > 65535) return 'Choose a port between 1024 and 65535.';
  for (const [which, password] of [['admin', form.adminPassword], ['StageZero', form.appPassword]] as const) {
    if (password.length < MIN_PASSWORD_LENGTH) return `The ${which} password needs at least ${MIN_PASSWORD_LENGTH} characters.`;
    if (/[\s;'"\\]/.test(password) || !/^[\x21-\x7e]+$/.test(password)) return `The ${which} password can't contain spaces, ; ' " or \\.`;
  }
  if (form.adminPassword === form.appPassword) return 'Use two different passwords.';
  if (!form.recorded) return 'Confirm that you have recorded both passwords.';
  return null;
}

export const RESTART_POLL_MS = 2000;
export const RESTART_ATTEMPTS = 90;

/**
 * Waits for the restarted app to answer on the expected database: failures (the port is
 * closed while it restarts) and the old process still winding down are both "not yet".
 * Resolves with the new status, or null when it gave up.
 */
export async function waitForDatabase(
  expected: DatabaseProvider,
  poll: () => Promise<DatabaseStatus>,
  sleep: (ms: number) => Promise<void> = (ms) => new Promise((r) => setTimeout(r, ms)),
  attempts = RESTART_ATTEMPTS,
): Promise<DatabaseStatus | null> {
  for (let i = 0; i < attempts; i++) {
    try {
      const status = await poll();
      if (!status.restartPending && status.provider === expected) return status;
    } catch {
      // Not back yet.
    }
    await sleep(RESTART_POLL_MS);
  }
  return null;
}

type Mode = 'existing' | 'create';

const MODES = [
  { value: 'existing', label: 'Use an existing server' },
  { value: 'create', label: 'Create a new container' },
];

export function DatabasePage() {
  useDocumentTitle('Database');
  const notify = useToast();
  const [status, setStatus] = useState<DatabaseStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [restarting, setRestarting] = useState<{ message: string; expected: DatabaseProvider } | null>(null);
  const [gaveUp, setGaveUp] = useState(false);
  const [confirming, setConfirming] = useState<'switch' | 'revert' | null>(null);

  // Finding a server, or making one.
  const [discovery, setDiscovery] = useState<PostgresDiscovery | null>(null);
  const [discovering, setDiscovering] = useState(false);
  const [mode, setMode] = useState<Mode>('existing');
  const [port, setPort] = useState('5432');
  const [adminPassword, setAdminPassword] = useState('');
  const [appPassword, setAppPassword] = useState('');
  const [showPasswords, setShowPasswords] = useState(false);
  const [backupFolder, setBackupFolder] = useState('');
  const [publishEverywhere, setPublishEverywhere] = useState(false);
  const [recorded, setRecorded] = useState(false);
  const [created, setCreated] = useState<CreatedPostgres | null>(null);

  // Connecting to it.
  const [connection, setConnection] = useState('');
  const [showConnection, setShowConnection] = useState(false);
  const [copyData, setCopyData] = useState(true);
  const [tested, setTested] = useState<{ for: string; result: ConnectionTest } | null>(null);

  const load = useCallback(() => api.database.status()
    .then((s) => { setStatus(s); setCopyData(s.sqliteFileExists && s.provider === 'sqlite'); })
    .catch((err: Error) => setError(err.message)), []);

  useEffect(() => { void load(); }, [load]);

  const run = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await work(); } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  const discover = async () => {
    setDiscovering(true);
    setError(null);
    try {
      const found = await api.database.discoverPostgres();
      setDiscovery(found);
      setPort(String(found.suggestedPort));
      setBackupFolder(found.backupFolder);
      setPublishEverywhere(!found.inContainer);
      if (found.servers.length === 0 && !found.container && found.docker.available) setMode('create');
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setDiscovering(false);
    }
  };

  const testConnection = async (value: string) => {
    const result = await api.database.test(value.trim());
    setTested({ for: value.trim(), result });
  };

  const test = () => run(() => testConnection(connection));

  const pickServer = (host: string, serverPort: number) => {
    setMode('existing');
    setConnection(connectionStringFor(host, serverPort));
    setTested(null);
    setCreated(null);
  };

  const create = () => run(async () => {
    const result = await api.database.createPostgres({
      port: Number(port),
      adminPassword,
      appPassword,
      backupFolder: backupFolder.trim() || null,
      publishOnAllInterfaces: publishEverywhere,
    });
    setCreated(result);
    notify(`PostgreSQL container ${result.containerName} is running`, 'success');
    const value = connectionStringFor(result.host, result.port, appPassword);
    setConnection(value);
    setAdminPassword('');
    setAppPassword('');
    setRecorded(false);
    setMode('existing');
    await testConnection(value);
  });

  const awaitRestart = async (message: string, expected: DatabaseProvider) => {
    setRestarting({ message, expected });
    setGaveUp(false);
    const back = await waitForDatabase(expected, () => api.database.status());
    if (back) {
      setStatus(back);
      setRestarting(null);
      setConnection('');
      setTested(null);
      setDiscovery(null);
      notify(`StageZero is back, on ${PROVIDER_LABEL[back.provider]}`, 'success');
    } else {
      setGaveUp(true);
    }
  };

  const switchDatabase = () => run(async () => {
    setConfirming(null);
    const result = await api.database.switchToPostgres(connection.trim(), copyData);
    await awaitRestart(result.message, 'postgres');
  });

  const revert = () => run(async () => {
    setConfirming(null);
    const result = await api.database.revertToSqlite();
    await awaitRestart(result.message, 'sqlite');
  });

  if (!status && !error) return <Loading />;

  const trimmed = connection.trim();
  const testedCurrent = tested && tested.for === trimmed ? tested.result : null;
  const summary = testedCurrent ? testSummary(testedCurrent, copyData) : null;
  const fromSqlite = status?.provider === 'sqlite';
  const problem = createProblem({ port, adminPassword, appPassword, recorded });
  const container = discovery?.container ?? null;
  const containerHost = discovery?.inContainer ? container?.name ?? '' : 'localhost';
  const containerPort = discovery?.inContainer ? 5432 : container?.hostPort ?? null;

  return (
    <div className="sz-page sz-page-narrow">
      <PageHeader title="Database">
        Where StageZero keeps its accounts, DNS records, tunnel routes and settings. SQLite needs nothing;
        PostgreSQL suits a server you already back up.
      </PageHeader>

      {error ? <Notice tone="danger">{error}</Notice> : null}

      {restarting ? (
        <Card role="status">
          <div className="sz-stack">
            <h2>Restarting</h2>
            <p className="sz-small">{restarting.message}</p>
            {gaveUp ? (
              <Notice tone="warning" title="StageZero hasn't answered yet"
                action={<Button size="sm" onClick={() => void awaitRestart(restarting.message, restarting.expected)}>Check again</Button>}>
                {status?.restartsAutomatically
                  ? 'The container should restart on its own; check docker compose logs stagezero if it has not.'
                  : 'StageZero is not running under Docker, so start it again by hand, then check here.'}
              </Notice>
            ) : (
              <Loading label={status?.restartsAutomatically
                ? 'Waiting for the container to come back…'
                : 'Waiting for StageZero to come back. Not under Docker? Start it again by hand.'} />
            )}
          </div>
        </Card>
      ) : null}

      {status ? (
        <Card>
          <div className="sz-stack">
            <h2>Current database</h2>
            <div className="sz-row">
              <Badge tone={status.provider === 'postgres' ? 'primary' : 'accent'}>{PROVIDER_LABEL[status.provider]}</Badge>
              <span className="sz-mono sz-small sz-break">{status.description}</span>
            </div>
            <p className="sz-small sz-muted">{sourceExplanation(status)}</p>
            {status.source === 'settings' && status.canChange && !restarting ? (
              <div className="sz-row">
                <Button variant="ghost" size="sm" disabled={busy} onClick={() => setConfirming('revert')}>Go back to SQLite</Button>
              </div>
            ) : null}
          </div>
        </Card>
      ) : null}

      {status?.canChange && !restarting ? (
        <Card>
          <div className="sz-stack">
            <h2>{fromSqlite ? 'Use PostgreSQL' : 'Change the PostgreSQL connection'}</h2>
            <Notice title="What happens">
              {fromSqlite
                ? 'StageZero tests the connection, copies every table into the empty database if you ask it to, saves the '
                  + 'connection string encrypted, and restarts on PostgreSQL. The SQLite file stays where it is, unchanged, as a fallback.'
                : 'StageZero tests the connection, brings that database to the current schema, saves the connection string '
                  + 'encrypted, and restarts on it. Nothing is copied between the two PostgreSQL databases.'}
              {' '}The data directory is still needed either way: it holds the keys that decrypt the stored Cloudflare tokens.
            </Notice>

            <h3>1. Is there a PostgreSQL server already?</h3>
            <div className="sz-row">
              <Button variant="outline" disabled={busy || discovering} onClick={() => void discover()}>
                {discovering ? 'Looking…' : discovery ? 'Look again' : 'Look for PostgreSQL on this machine'}
              </Button>
              <span className="sz-xs sz-muted">Tries the standard ports 5432–5439{discovery?.inContainer ? ' on the Docker host' : ''}.</span>
            </div>

            {discovery ? (
              <div className="sz-stack-sm" aria-label="Servers found">
                {discovery.servers.length === 0 && !container ? (
                  <Notice>No PostgreSQL server answered on the standard ports. Create one below, or enter the connection to a server elsewhere.</Notice>
                ) : null}
                {container ? (
                  <ServerRow label={`StageZero's own container (${container.status}${container.hostPort ? `, host port ${container.hostPort}` : ''})`}
                    address={containerPort ? `${containerHost}:${containerPort}` : container.name} disabled={busy || !container.running || !containerPort}
                    onUse={() => pickServer(containerHost, containerPort ?? 5432)} />
                ) : null}
                {discovery.servers.filter((s) => !(container && s.host === container.name)).map((s) => (
                  <ServerRow key={`${s.host}:${s.port}`} label={`PostgreSQL on ${s.label}`} address={`${s.host}:${s.port}`} disabled={busy}
                    onUse={() => pickServer(s.host, s.port)} />
                ))}
                <p className="sz-xs sz-muted">
                  Docker: {discovery.docker.available ? discovery.docker.detail : 'not available'}
                </p>
              </div>
            ) : null}

            <h3>2. Which server should StageZero use?</h3>
            <Segmented name="Server" options={MODES} value={mode} onChange={(v) => setMode(v as Mode)} />

            {mode === 'create' ? (
              <div className="sz-stack">
                {discovery && !discovery.docker.available ? (
                  <Notice tone="warning" title="StageZero can't create containers here">{discovery.docker.detail}</Notice>
                ) : null}
                {!discovery ? <Notice>Look for servers first: that also checks whether Docker is reachable and suggests a free port.</Notice> : null}
                <Notice tone="warning" title="Record both passwords now">
                  StageZero shows them only while you type them. It never stores the admin password, and keeps the StageZero one
                  only inside the encrypted connection string once you switch. Put both in your password manager before you create the server.
                </Notice>
                <Input label="Port on this machine" type="number" inputMode="numeric" min={1024} max={65535} value={port} disabled={busy}
                  className="sz-mono" onChange={(e) => setPort(e.target.value)}
                  hint={discovery ? `${discovery.suggestedPort} is free. 5432 is the usual one.` : '5432 is the usual one.'} />
                <PasswordField label="Admin password (postgres superuser)" value={adminPassword} show={showPasswords} disabled={busy}
                  onChange={setAdminPassword} hint="Unlocks the whole server. Only you hold it." />
                <PasswordField label="StageZero password (role stagezero)" value={appPassword} show={showPasswords} disabled={busy}
                  onChange={setAppPassword} hint="Owns only the stagezero database; this is what the app connects with." />
                <Checkbox label="Show the passwords" checked={showPasswords} disabled={busy} onChange={(e) => setShowPasswords(e.target.checked)} />
                <Input label="Backups folder" value={backupFolder} disabled={busy} className="sz-mono" onChange={(e) => setBackupFolder(e.target.value)}
                  hint={`A folder on the machine running Docker, mounted into the container at /backups. It belongs to you, not to compose: down, down -v and prune never touch it. The data itself goes in ${discovery?.dataFolder ?? 'a postgres folder beside StageZero\u2019s data'}.`} />
                <Checkbox label="Reachable from other machines on the network" checked={publishEverywhere} disabled={busy}
                  onChange={(e) => setPublishEverywhere(e.target.checked)} />
                <span className="sz-xs sz-muted">
                  Off: the port answers on 127.0.0.1 only. On: on every interface, which some desktop engines (Colima, Lima) need before the host itself can reach it.
                </span>
                <Checkbox label="I have recorded both passwords" checked={recorded} disabled={busy} onChange={(e) => setRecorded(e.target.checked)} />
                <div className="sz-row">
                  <Button disabled={busy || problem !== null || (discovery !== null && !discovery.docker.available)} onClick={() => void create()}>
                    {busy ? 'Creating…' : 'Create the container'}
                  </Button>
                  {problem ? <span className="sz-xs sz-muted">{problem}</span> : null}
                </div>
              </div>
            ) : (
              <div className="sz-stack">
                {created ? (
                  <Notice tone="success" title={`Container ${created.containerName} is running`}>
                    PostgreSQL on host port {created.hostPort}; data in <span className="sz-mono">{created.dataFolder}</span>, backups in{' '}
                    <span className="sz-mono">{created.backupFolder}</span>. The connection below is filled in with the StageZero password you chose.
                  </Notice>
                ) : null}
                <Input label="Connection string" type={showConnection ? 'text' : 'password'} autoComplete="off" className="sz-mono"
                  placeholder="Host=postgres;Port=5432;Database=stagezero;Username=stagezero;Password=…"
                  value={connection} disabled={busy} onChange={(e) => setConnection(e.target.value)}
                  hint="Stored encrypted and never shown again. No ';' in the password: it would end the Password field." />
                <Checkbox label="Show the connection string" checked={showConnection} disabled={busy}
                  onChange={(e) => setShowConnection(e.target.checked)} />
                <div className="sz-row">
                  <Button variant="outline" disabled={busy || !trimmed} onClick={() => void test()}>{busy ? 'Working…' : 'Test connection'}</Button>
                </div>
                {testedCurrent && summary ? (
                  <Notice tone={summary.tone}
                    title={`Connected to PostgreSQL ${testedCurrent.serverVersion} on ${testedCurrent.host}:${testedCurrent.port}`}>
                    Database <span className="sz-mono">{testedCurrent.database}</span> as <span className="sz-mono">{testedCurrent.username}</span>. {summary.text}
                  </Notice>
                ) : null}
                {fromSqlite && status.sqliteFileExists ? (
                  <Checkbox label="Copy my SQLite data into it" checked={copyData} disabled={busy}
                    onChange={(e) => setCopyData(e.target.checked)} />
                ) : null}
                <div className="sz-row">
                  <Button disabled={busy || !testedCurrent} onClick={() => setConfirming('switch')}>Switch and restart</Button>
                  {!testedCurrent ? <span className="sz-xs sz-muted">Test the connection first.</span> : null}
                </div>
              </div>
            )}
          </div>
        </Card>
      ) : null}

      <ConfirmDialog open={confirming === 'switch'} busy={busy} title="Switch to PostgreSQL and restart" confirmLabel="Switch and restart"
        message={copyData && fromSqlite
          ? 'StageZero copies its data into the PostgreSQL database, saves the connection, and stops. Under Docker the container restarts on its own; otherwise start it again by hand. The SQLite file is left as it is.'
          : 'StageZero saves the connection and stops. Under Docker the container restarts on its own; otherwise start it again by hand. Nothing is copied.'}
        onCancel={() => setConfirming(null)} onConfirm={() => void switchDatabase()} />

      <ConfirmDialog open={confirming === 'revert'} busy={busy} title="Go back to SQLite" confirmLabel="Go back and restart"
        message="StageZero forgets the PostgreSQL connection and restarts on the SQLite file in the data directory, as it was when you switched. Anything changed since then stays in PostgreSQL."
        onCancel={() => setConfirming(null)} onConfirm={() => void revert()} />
    </div>
  );
}

function ServerRow({ label, address, disabled, onUse }: { label: string; address: string; disabled: boolean; onUse: () => void }) {
  return (
    <div className="sz-row-between">
      <div className="sz-stack-sm">
        <span className="sz-small sz-strong">{label}</span>
        <span className="sz-mono sz-xs sz-muted">{address}</span>
      </div>
      <Button variant="outline" size="sm" disabled={disabled} onClick={onUse}>Use this server</Button>
    </div>
  );
}

function PasswordField({ label, value, show, disabled, hint, onChange }: {
  label: string; value: string; show: boolean; disabled: boolean; hint: string; onChange: (value: string) => void;
}) {
  return (
    <div className="sz-row" style={{ alignItems: 'flex-end' }}>
      <div style={{ flex: 1, minWidth: 0 }}>
        <Input label={label} type={show ? 'text' : 'password'} autoComplete="new-password" className="sz-mono" value={value} disabled={disabled}
          onChange={(e) => onChange(e.target.value)} hint={hint} />
      </div>
      <Button variant="outline" size="sm" disabled={disabled} onClick={() => onChange(generatePassword())}>Generate</Button>
    </div>
  );
}
