import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { DatabaseStatus, PostgresDiscovery, PostgresTestResult } from '../api/types';
import { server } from '../test/server';
import { owner, renderApp, sessionInfo } from '../test/render';
import { createProblem, generatePassword } from './PostgresProvision';

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

describe('finding a PostgreSQL on the machine', () => {
  it('lists what it finds and fills the form in for the chosen server', async () => {
    settingsPage();
    server.use(http.get('/api/settings/database/postgres/discover', () => HttpResponse.json({
      ...nothingFound,
      servers: [{ host: 'host.docker.internal', port: 5433, label: 'this machine' }],
      container: { name: 'stagezero-postgres', status: 'running', running: true, hostPort: 5434 },
      suggestedPort: 5435,
    })));
    renderApp('/settings');
    const user = userEvent.setup();

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
    renderApp('/settings');
    const user = userEvent.setup();

    // Nothing found and Docker there: the card moves to "create" by itself.
    await user.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));
    expect(await screen.findByText(/No PostgreSQL server answered/)).toBeInTheDocument();
    expect(screen.getByText(/Record both passwords now/)).toBeInTheDocument();
    const create = screen.getByRole('button', { name: 'Create the container' });
    expect(create).toBeDisabled();

    await user.type(screen.getByLabelText(/^Admin password/), GOOD_ADMIN);
    await user.type(screen.getByLabelText(/^StageZero password/), GOOD_APP);
    await user.clear(screen.getByLabelText(/^Backups folder/));
    await user.type(screen.getByLabelText(/^Backups folder/), '/mnt/safe/backups');
    expect(create).toBeDisabled(); // still not recorded
    expect(screen.getByText('Confirm that you have recorded both passwords.')).toBeInTheDocument();
    await user.click(screen.getByLabelText('I have recorded both passwords'));
    expect(create).toBeEnabled();
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
    renderApp('/settings');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));
    await screen.findByText(/No PostgreSQL server answered/);
    await user.click(screen.getByRole('radio', { name: 'Create a new container' }));

    expect(await screen.findByText(/DOCKER_GID=101/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create the container' })).toBeDisabled();
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

  it('refuses what the server would refuse, before the round trip', () => {
    expect(createProblem(good)).toBeNull();
    expect(createProblem({ ...good, port: '80' })).toMatch(/between 1024/);
    expect(createProblem({ ...good, adminPassword: 'short' })).toMatch(/admin password needs at least 12/);
    expect(createProblem({ ...good, appPassword: 'has a space in it' })).toMatch(/StageZero password can't contain/);
    expect(createProblem({ ...good, appPassword: 'semi;colon-password' })).toMatch(/can't contain/);
    expect(createProblem({ ...good, appPassword: GOOD_ADMIN })).toMatch(/two different/);
    expect(createProblem({ ...good, recorded: false })).toMatch(/recorded both/);
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
