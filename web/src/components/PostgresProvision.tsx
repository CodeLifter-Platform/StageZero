import { useState } from 'react';
import type { ProvisioningApi } from '../api/client';
import type { CreatedPostgres, PostgresDiscovery } from '../api/types';
import { Button, Checkbox, Input, Segmented } from '../ui';
import { Notice } from './Notice';

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

type Mode = 'existing' | 'create';

const MODES = [
  { value: 'existing', label: 'Use an existing server' },
  { value: 'create', label: 'Create a new container' },
];

/**
 * The two steps before the connection form: is there a PostgreSQL on this machine already
 * (the standard ports, probed by the server), and if not — or anyway — create StageZero's own
 * container, with the operator's port, two passwords and a backups folder of their own.
 */
export function PostgresProvision({ provisioning, disabled, onUseServer, onCreated, onError }: {
  provisioning: ProvisioningApi;
  disabled: boolean;
  onUseServer: (host: string, port: number) => void;
  onCreated: (created: CreatedPostgres, appPassword: string) => void;
  onError: (message: string | null) => void;
}) {
  const [discovery, setDiscovery] = useState<PostgresDiscovery | null>(null);
  const [busy, setBusy] = useState<'discover' | 'create' | null>(null);
  const [mode, setMode] = useState<Mode>('existing');
  const [port, setPort] = useState('5432');
  const [adminPassword, setAdminPassword] = useState('');
  const [appPassword, setAppPassword] = useState('');
  const [showPasswords, setShowPasswords] = useState(false);
  const [backupFolder, setBackupFolder] = useState('');
  const [publishEverywhere, setPublishEverywhere] = useState(false);
  const [recorded, setRecorded] = useState(false);
  const [created, setCreated] = useState<CreatedPostgres | null>(null);

  const off = disabled || busy !== null;

  const discover = async () => {
    setBusy('discover');
    onError(null);
    try {
      const found = await provisioning.discover();
      setDiscovery(found);
      setPort(String(found.suggestedPort));
      setBackupFolder(found.backupFolder);
      setPublishEverywhere(!found.inContainer);
      if (found.servers.length === 0 && !found.container && found.docker.available) setMode('create');
    } catch (err) {
      onError((err as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const create = async () => {
    setBusy('create');
    onError(null);
    try {
      const result = await provisioning.create({
        port: Number(port),
        adminPassword,
        appPassword,
        backupFolder: backupFolder.trim() || null,
        publishOnAllInterfaces: publishEverywhere,
      });
      setCreated(result);
      setMode('existing');
      const password = appPassword;
      setAdminPassword('');
      setAppPassword('');
      setRecorded(false);
      onCreated(result, password);
    } catch (err) {
      onError((err as Error).message);
    } finally {
      setBusy(null);
    }
  };

  const pick = (host: string, serverPort: number) => {
    setMode('existing');
    setCreated(null);
    onUseServer(host, serverPort);
  };

  const problem = createProblem({ port, adminPassword, appPassword, recorded });
  const container = discovery?.container ?? null;
  const containerHost = discovery?.inContainer ? container?.name ?? '' : 'localhost';
  const containerPort = discovery?.inContainer ? 5432 : container?.hostPort ?? null;

  return (
    <div className="sz-stack">
      <h3>1. Is there a PostgreSQL server already?</h3>
      <div className="sz-row">
        <Button variant="outline" disabled={off} onClick={() => void discover()}>
          {busy === 'discover' ? 'Looking…' : discovery ? 'Look again' : 'Look for PostgreSQL on this machine'}
        </Button>
        <span className="sz-xs sz-muted">Tries the standard ports 5432–5439{discovery?.inContainer ? ' on the Docker host' : ''}.</span>
      </div>

      {discovery ? (
        <div className="sz-stack-sm" aria-label="Servers found">
          {discovery.servers.length === 0 && !container ? (
            <Notice>No PostgreSQL server answered on the standard ports. Create one below, or fill in a server elsewhere.</Notice>
          ) : null}
          {container ? (
            <ServerRow label={`StageZero's own container (${container.status}${container.hostPort ? `, host port ${container.hostPort}` : ''})`}
              address={containerPort ? `${containerHost}:${containerPort}` : container.name} disabled={off || !container.running || !containerPort}
              onUse={() => pick(containerHost, containerPort ?? 5432)} />
          ) : null}
          {discovery.servers.filter((s) => !(container && s.host === container.name)).map((s) => (
            <ServerRow key={`${s.host}:${s.port}`} label={`PostgreSQL on ${s.label}`} address={`${s.host}:${s.port}`} disabled={off}
              onUse={() => pick(s.host, s.port)} />
          ))}
          <p className="sz-xs sz-muted">Docker: {discovery.docker.available ? discovery.docker.detail : 'not available'}</p>
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
            only inside the encrypted connection once you switch. Put both in your password manager before you create the server.
          </Notice>
          <Input label="Port on this machine" type="number" inputMode="numeric" min={1024} max={65535} value={port} disabled={off}
            className="sz-mono" onChange={(e) => setPort(e.target.value)}
            hint={discovery ? `${discovery.suggestedPort} is free. 5432 is the usual one.` : '5432 is the usual one.'} />
          <PasswordField label="Admin password (postgres superuser)" value={adminPassword} show={showPasswords} disabled={off}
            onChange={setAdminPassword} hint="Unlocks the whole server. Only you hold it." />
          <PasswordField label="StageZero password (role stagezero)" value={appPassword} show={showPasswords} disabled={off}
            onChange={setAppPassword} hint="Owns only the stagezero database; this is what the app connects with." />
          <Checkbox label="Show the passwords" checked={showPasswords} disabled={off} onChange={(e) => setShowPasswords(e.target.checked)} />
          <Input label="Backups folder" value={backupFolder} disabled={off} className="sz-mono" onChange={(e) => setBackupFolder(e.target.value)}
            hint={`A folder on the machine running Docker, mounted into the container at /backups. It belongs to you, not to compose: down, down -v and prune never touch it. The data itself goes in ${discovery?.dataFolder ?? 'a postgres folder beside StageZero\u2019s data'}.`} />
          <Checkbox label="Reachable from other machines on the network" checked={publishEverywhere} disabled={off}
            onChange={(e) => setPublishEverywhere(e.target.checked)} />
          <span className="sz-xs sz-muted">
            Off: the port answers on 127.0.0.1 only. On: on every interface, which some desktop engines (BoxCar, Colima) need before the host itself can reach it.
          </span>
          <Checkbox label="I have recorded both passwords" checked={recorded} disabled={off} onChange={(e) => setRecorded(e.target.checked)} />
          <div className="sz-row">
            <Button disabled={off || problem !== null || (discovery !== null && !discovery.docker.available)} onClick={() => void create()}>
              {busy === 'create' ? 'Creating…' : 'Create the container'}
            </Button>
            {problem ? <span className="sz-xs sz-muted">{problem}</span> : null}
          </div>
        </div>
      ) : created ? (
        <Notice tone="success" title={`Container ${created.containerName} is running`}>
          PostgreSQL on host port {created.hostPort}; data in <span className="sz-mono">{created.dataFolder}</span>, backups in{' '}
          <span className="sz-mono">{created.backupFolder}</span>. The form below is filled in with the StageZero password you chose.
        </Notice>
      ) : null}
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
