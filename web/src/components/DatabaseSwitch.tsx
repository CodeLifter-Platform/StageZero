import { useEffect, useState } from 'react';
import type { DatabaseApi } from '../api/client';
import type { DatabaseStatus, PostgresTarget, PostgresTestResult } from '../api/types';
import { Button, Input, Select } from '../ui';
import { ConfirmDialog } from './ConfirmDialog';
import { Notice } from './Notice';

const EMPTY: PostgresTarget = { host: '', port: 5432, database: 'stagezero', username: 'stagezero', password: '', sslMode: 'Prefer' };

/**
 * Polls until the app answers on PostgreSQL after its restart. Errors while it is down are
 * expected and ignored. Resolves false if it never comes back within the timeout.
 */
export async function waitForPostgres(
  status: () => Promise<DatabaseStatus>,
  { intervalMs = 2000, timeoutMs = 120_000, sleep = (ms: number) => new Promise((r) => setTimeout(r, ms)) } = {},
): Promise<boolean> {
  for (let waited = 0; waited < timeoutMs; waited += intervalMs) {
    await sleep(intervalMs);
    try {
      if ((await status()).provider === 'postgres') return true;
    } catch {
      // Still restarting.
    }
  }
  return false;
}

/**
 * Moving StageZero from its SQLite file onto a PostgreSQL database that runs outside the
 * app (the platform Infra stack, or any server). Test, then copy and switch: one way, into an
 * empty database only, then the app restarts onto it. Used by Settings and the first-run page.
 */
export function DatabaseSwitch({ databases, onSwitched }: { databases: DatabaseApi; onSwitched?: () => void }) {
  const [status, setStatus] = useState<DatabaseStatus | null>(null);
  const [target, setTarget] = useState<PostgresTarget>(EMPTY);
  const [tested, setTested] = useState<{ for: PostgresTarget; result: PostgresTestResult } | null>(null);
  const [busy, setBusy] = useState<'test' | 'switch' | 'restarting' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);

  useEffect(() => {
    databases.status().then(setStatus).catch((err: Error) => setError(err.message));
  }, [databases]);

  const set = <K extends keyof PostgresTarget>(key: K, value: PostgresTarget[K]) => {
    setTarget((t) => ({ ...t, [key]: value }));
    setTested(null);
  };

  const test = async () => {
    setBusy('test');
    setError(null);
    try {
      setTested({ for: target, result: await databases.test(target) });
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const switchTo = async () => {
    setConfirming(false);
    setBusy('switch');
    setError(null);
    try {
      await databases.switchTo(target);
      setBusy('restarting');
      if (await waitForPostgres(databases.status)) {
        setStatus(await databases.status());
        setTarget(EMPTY);
        setTested(null);
        onSwitched?.();
      } else {
        setError('StageZero switched and stopped, but hasn\'t come back yet. Start it again (docker compose up -d), then reload this page.');
      }
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(null);
    }
  };

  if (!status) return error ? <Notice tone="danger">{error}</Notice> : null;

  if (!status.canSwitch) {
    return (
      <div className="sz-stack-sm">
        <dl className="sz-kv">
          <dt>Database</dt><dd className="sz-break">{status.description}</dd>
          <dt>Chosen</dt>
          <dd>{status.source === 'environment' ? 'By STAGEZERO_DB_PROVIDER / STAGEZERO_DB_CONNECTION' : 'Here, in the web app'}</dd>
        </dl>
        <span className="sz-xs sz-muted">
          {status.source === 'environment'
            ? 'Set in the environment, so it can\'t be changed here. Change those variables and restart.'
            : 'Switching runs one way. The SQLite file from before is kept as it was; going back is a manual recovery step (OnboardDocker.md).'}
        </span>
      </div>
    );
  }

  const result = tested && tested.for === target ? tested.result : null;
  const disabled = busy !== null;

  return (
    <div className="sz-stack">
      <dl className="sz-kv">
        <dt>Database</dt><dd className="sz-break">{status.description}</dd>
      </dl>
      <p className="sz-small sz-muted">
        Move to PostgreSQL running outside StageZero, so nothing done to StageZero's container can touch the data. On a
        server with the platform Infra stack, create a database with <code>tools/create-app-db.sh prod stagezero</code> and
        use host <code>host.docker.internal</code>. The database must be empty: StageZero copies everything into it, then
        restarts onto it. The SQLite file is kept as it was. This runs one way.
      </p>
      <div className="sz-grid">
        <Input label="Host" placeholder="host.docker.internal" value={target.host} disabled={disabled} onChange={(e) => set('host', e.target.value)} />
        <Input label="Port" type="number" value={String(target.port)} disabled={disabled} onChange={(e) => set('port', Number(e.target.value) || 0)} />
        <Input label="Database" value={target.database} disabled={disabled} onChange={(e) => set('database', e.target.value)} />
        <Input label="Username" value={target.username} disabled={disabled} autoComplete="off" onChange={(e) => set('username', e.target.value)} />
        <Input label="Password" type="password" value={target.password} disabled={disabled} autoComplete="new-password" onChange={(e) => set('password', e.target.value)} />
        <Select label="SSL" value={target.sslMode} disabled={disabled} onChange={(e) => set('sslMode', e.target.value as PostgresTarget['sslMode'])}>
          <option value="Prefer">Prefer</option>
          <option value="Require">Require</option>
          <option value="Disable">Disable</option>
        </Select>
      </div>

      {result ? (
        result.ok ? (
          <Notice tone="success" title={`Connected: PostgreSQL ${result.serverVersion ?? ''}`}>
            The database is empty and StageZero can create its tables.
            {result.warnings.map((w) => <span key={w}><br />{w}</span>)}
          </Notice>
        ) : (
          <Notice tone="danger" title="Can't use this database">{result.problem}</Notice>
        )
      ) : null}
      {busy === 'restarting' ? (
        <Notice tone="info" title="Restarting on PostgreSQL…">The data is copied. StageZero is restarting onto the new database; this page continues when it's back.</Notice>
      ) : null}
      {error ? <Notice tone="danger">{error}</Notice> : null}

      <div className="sz-row">
        <Button variant="outline" disabled={disabled || !target.host.trim() || !target.password} onClick={() => void test()}>
          {busy === 'test' ? 'Testing…' : 'Test connection'}
        </Button>
        <Button disabled={disabled || !result?.ok} onClick={() => setConfirming(true)}>
          {busy === 'switch' ? 'Copying…' : 'Copy and switch'}
        </Button>
      </div>

      <ConfirmDialog open={confirming} title="Switch to PostgreSQL" confirmLabel="Copy and switch"
        message={`Copy everything into ${target.database} on ${target.host} and restart StageZero onto it? `
          + 'The SQLite file is kept as it was, but StageZero won\'t use it again: switching runs one way.'}
        onCancel={() => setConfirming(false)} onConfirm={() => void switchTo()} />
    </div>
  );
}
