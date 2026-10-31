import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { ConnectionTest, DatabaseProvider, DatabaseStatus } from '../../api/types';
import { useToast } from '../../app/toast';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { PageHeader } from '../../components/PageHeader';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Badge, Button, Card, Checkbox, Input } from '../../ui';

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

export function DatabasePage() {
  useDocumentTitle('Database');
  const notify = useToast();
  const [status, setStatus] = useState<DatabaseStatus | null>(null);
  const [connection, setConnection] = useState('');
  const [showConnection, setShowConnection] = useState(false);
  const [copyData, setCopyData] = useState(true);
  const [tested, setTested] = useState<{ for: string; result: ConnectionTest } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [restarting, setRestarting] = useState<{ message: string; expected: DatabaseProvider } | null>(null);
  const [gaveUp, setGaveUp] = useState(false);
  const [confirming, setConfirming] = useState<'switch' | 'revert' | null>(null);

  const load = useCallback(() => api.database.status()
    .then((s) => { setStatus(s); setCopyData(s.sqliteFileExists && s.provider === 'sqlite'); })
    .catch((err: Error) => setError(err.message)), []);

  useEffect(() => { void load(); }, [load]);

  const run = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await work(); } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  const test = () => run(async () => {
    const result = await api.database.test(connection.trim());
    setTested({ for: connection.trim(), result });
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
            <h2>{fromSqlite ? 'Switch to PostgreSQL' : 'Change the PostgreSQL connection'}</h2>
            <Notice title="What happens">
              {fromSqlite
                ? 'StageZero tests the connection, copies every table into the empty database if you ask it to, saves the '
                  + 'connection string encrypted, and restarts on PostgreSQL. The SQLite file stays where it is, unchanged, as a fallback.'
                : 'StageZero tests the connection, brings that database to the current schema, saves the connection string '
                  + 'encrypted, and restarts on it. Nothing is copied between the two PostgreSQL databases.'}
              {' '}The data directory is still needed either way: it holds the keys that decrypt the stored Cloudflare tokens.
            </Notice>
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
            <p className="sz-xs sz-muted">
              No PostgreSQL server yet? In the StageZero checkout, <code>docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d</code>{' '}
              runs one beside the app and sets the variables for you, so this page isn't needed.
            </p>
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
