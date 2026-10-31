import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { DatabaseStatus, PostgresDiscovery, PostgresTestResult } from '../api/types';
import { server } from '../test/server';
import { owner, renderApp, sessionInfo } from '../test/render';
import { createProblem, createProblems, generatePassword } from './PostgresProvision';

const sqlite: DatabaseStatus = { provider: 'sqlite', source: 'default', description: 'SQLite /app-data/stagezero.db', canSwitch: true };
const usable: PostgresTestResult = { ok: true, problem: null, serverVersion: '18.0', tableCount: 0, hasData: false, warnings: [] };

const nothingFound: PostgresDiscovery = {
  servers: [],
  docker: { available: true, detail: 'Docker 28.0.0 via /var/run/docker.sock' },
  container: null,
  suggestedPort: 5432,
  dataFolder: '/srv/stagezero/postgres',
  backupFolder: '/srv/stagezero/backups',
  inContainer: true,
};

const GOOD_ADMIN = 'admin-password-123456';
const GOOD_APP = 'stagezero-password-1234';

function settingsPage() {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/settings/cloudflare', () => HttpResponse.json({
      status: { isConfigured: false, isReadable: false, accountId: null, accountName: null, tokenId: null, expiresOn: null, verifiedAt: null, updatedAt: null },
      required: [], checks: null,
    })),
    http.get('/api/settings/email-updates', () => HttpResponse.json({ enabled: false, email: null, choices: [] })),
    http.get('/api/settings/database', () => HttpResponse.json(sqlite)),
  );
}

/** The Database section with PostgreSQL picked, which is where the two provisioning steps live. */
async function openPostgres() {
  renderApp('/settings?section=database');
  const user = userEvent.setup();
  await user.click(await screen.findByRole('radio', { name: 'PostgreSQL' }));
  return user;
}

describe('finding a PostgreSQL on the machine', () => {
  it('lists what it finds and fills the form in for the chosen server', async () => {
    settingsPage();
    server.use(http.get('/api/settings/database/postgres/discover', () => HttpResponse.json({
      ...nothingFound,
      servers: [{ host: 'host.docker.internal', port: 5433, label: 'this machine' }],
      container: { name: 'stagezero-postgres', status: 'running', running: true, hostPort: 5434 },
      suggestedPort: 5435,
    })));
    const user = await openPostgres();

    await user.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));

    expect(await screen.findByText('PostgreSQL on this machine')).toBeInTheDocument();
    expect(screen.getByText(/StageZero's own container \(running, host port 5434\)/)).toBeInTheDocument();
    const useButtons = screen.getAllByRole('button', { name: 'Use this server' });
    expect(useButtons).toHaveLength(2);
    await user.click(useButtons[1] as HTMLElement);

    expect(screen.getByLabelText(/^Host/)).toHaveValue('host.docker.internal');
    expect(screen.getByLabelText(/^Port$/)).toHaveValue(5433);
    expect(screen.getByLabelText(/^Password/)).toHaveValue(''); // still the operator's to enter
  });

  it('creates a container only after both passwords are recorded, then fills in and tests the form', async () => {
    settingsPage();
    let sent: unknown;
    let tested: Record<string, unknown> | undefined;
    server.use(
      http.get('/api/settings/database/postgres/discover', () => HttpResponse.json(nothingFound)),
      http.post('/api/settings/database/postgres/create', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({
          containerName: 'stagezero-postgres', host: 'stagezero-postgres', port: 5432, hostPort: 5432, database: 'stagezero',
          username: 'stagezero', dataFolder: '/srv/stagezero/postgres', backupFolder: '/mnt/safe/backups',
        });
      }),
      http.post('/api/settings/database/test', async ({ request }) => {
        tested = await request.json() as Record<string, unknown>;
        return HttpResponse.json(usable);
      }),
    );
    const user = await openPostgres();

    // Nothing found and Docker there: the card moves to "create" by itself.
    await user.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));
    expect(await screen.findByText(/No PostgreSQL server answered/)).toBeInTheDocument();
    expect(screen.getByText(/Record both passwords now/)).toBeInTheDocument();
    const create = screen.getByRole('button', { name: 'Create the container' });

    // An empty form, submitted: every field says what is wrong, and nothing is sent.
    await user.click(create);
    expect(screen.getAllByText('At least 12 characters.')).toHaveLength(2);
    expect(screen.getByText('Confirm that you have recorded both passwords.')).toBeInTheDocument();
    expect(screen.getByText('Fix the fields marked above.')).toBeInTheDocument();
    expect(sent).toBeUndefined();

    await user.type(screen.getByLabelText(/^Admin password/), GOOD_ADMIN);
    await user.type(screen.getByLabelText(/^StageZero password/), GOOD_APP);
    await user.clear(screen.getByLabelText(/^Backups folder/));
    await user.type(screen.getByLabelText(/^Backups folder/), '/mnt/safe/backups');
    await user.click(screen.getByLabelText('I have recorded both passwords'));
    expect(screen.queryByText('Fix the fields marked above.')).not.toBeInTheDocument();
    await user.click(create);

    expect(await screen.findByText('Container stagezero-postgres is running')).toBeInTheDocument();
    expect(sent).toEqual({ port: 5432, adminPassword: GOOD_ADMIN, appPassword: GOOD_APP, backupFolder: '/mnt/safe/backups', publishOnAllInterfaces: false });
    expect(screen.getByLabelText(/^Host/)).toHaveValue('stagezero-postgres');
    expect(screen.getByLabelText(/^Password/)).toHaveValue(GOOD_APP);
    await waitFor(() => expect(tested).toEqual({
      host: 'stagezero-postgres', port: 5432, database: 'stagezero', username: 'stagezero', password: GOOD_APP, sslMode: 'Prefer',
    }));
    expect(await screen.findByText('Connected: PostgreSQL 18.0')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy and switch' })).toBeEnabled();
  });

  it('explains when Docker is out of reach instead of offering to create', async () => {
    settingsPage();
    server.use(http.get('/api/settings/database/postgres/discover', () => HttpResponse.json({
      ...nothingFound, docker: { available: false, detail: 'The Docker socket /var/run/docker.sock is mounted, but StageZero\'s container user may not use it. It belongs to group 101: put DOCKER_GID=101 in .env and run the compose command again.' },
    })));
    const user = await openPostgres();

    await user.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));
    await screen.findByText(/No PostgreSQL server answered/);
    await user.click(screen.getByRole('radio', { name: 'Create a new container' }));

    expect(await screen.findByText(/DOCKER_GID=101/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create the container' })).toBeDisabled();
  });

  it('shows progress while the container is built, and the server\'s reason when it refuses', async () => {
    settingsPage();
    let release: (() => void) | undefined;
    server.use(
      http.get('/api/settings/database/postgres/discover', () => HttpResponse.json(nothingFound)),
      http.post('/api/settings/database/postgres/create', async () => {
        await new Promise<void>((resolve) => { release = resolve; });
        return HttpResponse.json(
          { title: 'The data folder /srv/stagezero/postgres already holds a PostgreSQL cluster, from an earlier container.' }, { status: 409 });
      }),
    );
    const user = await openPostgres();
    await user.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));
    await screen.findByText(/No PostgreSQL server answered/);
    await user.type(screen.getByLabelText(/^Admin password/), GOOD_ADMIN);
    await user.type(screen.getByLabelText(/^StageZero password/), GOOD_APP);
    await user.click(screen.getByLabelText('I have recorded both passwords'));

    await user.click(screen.getByRole('button', { name: 'Create the container' }));

    expect(await screen.findByText('Creating the PostgreSQL container')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Creating…' })).toBeDisabled();
    await waitFor(() => expect(release).toBeDefined());
    release?.();

    expect(await screen.findByText('The container was not created')).toBeInTheDocument();
    expect(screen.getByText(/already holds a PostgreSQL cluster/)).toBeInTheDocument();
    expect(screen.queryByText('Creating the PostgreSQL container')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create the container' })).toBeEnabled();
  });

  it('is not offered on the first-run page', async () => {
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ setupRequired: true }))),
      http.get('/api/setup/database', () => HttpResponse.json(sqlite)),
    );
    renderApp('/setup');
    const user = userEvent.setup();

    await user.click(await screen.findByLabelText("Keep StageZero's data in PostgreSQL"));

    expect(await screen.findByLabelText(/^Host/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Look for PostgreSQL on this machine' })).not.toBeInTheDocument();
  });
});

describe('the create form', () => {
  const good = { port: '5432', adminPassword: GOOD_ADMIN, appPassword: GOOD_APP, recorded: true };

  it('refuses what the server would refuse, field by field, before the round trip', () => {
    expect(createProblem(good)).toBeNull();
    expect(createProblems({ ...good, port: '80' })).toEqual({ port: 'Choose a port between 1024 and 65535.' });
    expect(createProblems({ ...good, adminPassword: 'short' })).toEqual({ adminPassword: 'At least 12 characters.' });
    expect(createProblems({ ...good, appPassword: 'has a space in it' }).appPassword).toMatch(/No spaces/);
    expect(createProblems({ ...good, appPassword: 'semi;colon-password' }).appPassword).toMatch(/No spaces, ;/);
    expect(createProblems({ ...good, appPassword: GOOD_ADMIN })).toEqual({ appPassword: 'Use a different password from the admin one.' });
    expect(createProblems({ ...good, recorded: false })).toEqual({ recorded: 'Confirm that you have recorded both passwords.' });
  });

  it('generates passwords the server, the connection string and a shell all accept', () => {
    const seen = new Set<string>();
    for (let i = 0; i < 50; i++) {
      const password = generatePassword();
      expect(password).toHaveLength(24);
      expect(createProblem({ ...good, adminPassword: password })).toBeNull();
      seen.add(password);
    }
    expect(seen.size).toBe(50);
  });
});
