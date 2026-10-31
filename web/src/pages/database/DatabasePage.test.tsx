import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ConnectionTest, DatabaseStatus, PostgresDiscovery } from '../../api/types';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';
import { connectionStringFor, createProblem, generatePassword, sourceExplanation, testSummary, waitForDatabase } from './DatabasePage';

const sqlite: DatabaseStatus = {
  provider: 'sqlite',
  description: 'SQLite /app-data/stagezero.db',
  source: 'default',
  canChange: true,
  restartPending: false,
  sqliteFileExists: true,
  restartsAutomatically: true,
};

const postgres: DatabaseStatus = {
  provider: 'postgres',
  description: 'PostgreSQL stagezero on db:5432 as stagezero',
  source: 'settings',
  canChange: true,
  restartPending: false,
  sqliteFileExists: true,
  restartsAutomatically: true,
};

const connected: ConnectionTest = {
  host: 'db', port: 5432, database: 'stagezero', username: 'stagezero', serverVersion: '18.0',
  databaseExists: true, isEmpty: true, hasStageZeroSchema: false,
};

/** The last of several same-named controls: a confirm dialog repeats the words of the button that opened it. */
function last<T>(items: T[]): T {
  const item = items.at(-1);
  if (item === undefined) throw new Error('nothing to pick from');
  return item;
}

const signedIn = () => http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner })));

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

describe('the Database page', () => {
  it('switches to PostgreSQL after a successful test, then follows the restart', { timeout: 10000 }, async () => {
    // The server's side of the story: SQLite, then the old process winding down (a pending
    // restart, or no answer at all), then the new process on PostgreSQL.
    let phase: 'sqlite' | 'restarting' | 'postgres' = 'sqlite';
    let sent: unknown;
    server.use(
      signedIn(),
      http.get('/api/database', () => {
        if (phase === 'sqlite') return HttpResponse.json(sqlite);
        if (phase === 'restarting') return HttpResponse.json({ ...sqlite, restartPending: true, canChange: false });
        return HttpResponse.json(postgres);
      }),
      http.post('/api/database/test', () => HttpResponse.json(connected)),
      http.post('/api/database/switch', async ({ request }) => {
        sent = await request.json();
        phase = 'restarting';
        return HttpResponse.json({ message: 'Copied 9 row(s). StageZero is restarting on it now.', copied: [{ table: 'User', rows: 1 }] });
      }),
    );
    renderApp('/database');

    expect(await screen.findByText('SQLite /app-data/stagezero.db')).toBeInTheDocument();
    const button = screen.getByRole('button', { name: 'Switch and restart' });
    expect(button).toBeDisabled(); // not before a test

    await userEvent.type(screen.getByLabelText(/^Connection string/), 'Host=db;Database=stagezero;Username=stagezero;Password=pw');
    await userEvent.click(screen.getByRole('button', { name: 'Test connection' }));
    expect(await screen.findByText('Connected to PostgreSQL 18.0 on db:5432')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Switch and restart' }));
    // The confirm dialog's own button carries the same words; it is the last one rendered.
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Switch and restart' })).toHaveLength(2));
    await userEvent.click(last(screen.getAllByRole('button', { name: 'Switch and restart' })));

    expect(await screen.findByText('Copied 9 row(s). StageZero is restarting on it now.')).toBeInTheDocument();
    expect(sent).toEqual({ connectionString: 'Host=db;Database=stagezero;Username=stagezero;Password=pw', copyData: true });
    expect(screen.queryByLabelText(/^Connection string/)).not.toBeInTheDocument(); // nothing to edit while it restarts

    phase = 'postgres';
    expect(await screen.findByText('PostgreSQL stagezero on db:5432 as stagezero', {}, { timeout: 6000 })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Go back to SQLite' })).toBeInTheDocument();
    expect(screen.queryByText(/restarting on it now/)).not.toBeInTheDocument();
  });

  it('lists the servers it finds and fills the connection in for the chosen one', async () => {
    server.use(
      signedIn(),
      http.get('/api/database', () => HttpResponse.json(sqlite)),
      http.get('/api/database/postgres/discover', () => HttpResponse.json({
        ...nothingFound,
        servers: [{ host: 'host.docker.internal', port: 5433, label: 'this machine' }],
        container: { name: 'stagezero-postgres', status: 'running', running: true, hostPort: 5434 },
        suggestedPort: 5435,
      })),
    );
    renderApp('/database');

    await userEvent.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));

    expect(await screen.findByText('PostgreSQL on this machine')).toBeInTheDocument();
    expect(screen.getByText(/StageZero's own container \(running, host port 5434\)/)).toBeInTheDocument();
    const useButtons = screen.getAllByRole('button', { name: 'Use this server' });
    expect(useButtons).toHaveLength(2);
    await userEvent.click(useButtons[1] as HTMLElement);

    expect(screen.getByLabelText(/^Connection string/)).toHaveValue('Host=host.docker.internal;Port=5433;Database=stagezero;Username=stagezero;Password=');
  });

  it('creates a container only after both passwords are recorded, then tests the new connection', async () => {
    let sent: unknown;
    let tests = 0;
    server.use(
      signedIn(),
      http.get('/api/database', () => HttpResponse.json(sqlite)),
      http.get('/api/database/postgres/discover', () => HttpResponse.json(nothingFound)),
      http.post('/api/database/postgres/create', async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({
          containerName: 'stagezero-postgres', host: 'stagezero-postgres', port: 5432, hostPort: 5432, database: 'stagezero',
          username: 'stagezero', dataFolder: '/srv/stagezero/postgres', backupFolder: '/mnt/safe/backups',
        });
      }),
      http.post('/api/database/test', () => { tests++; return HttpResponse.json(connected); }),
    );
    renderApp('/database');

    // Nothing found and Docker there: the page moves to "create" by itself.
    await userEvent.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));
    expect(await screen.findByText(/No PostgreSQL server answered/)).toBeInTheDocument();
    expect(screen.getByText(/Record both passwords now/)).toBeInTheDocument();
    const create = screen.getByRole('button', { name: 'Create the container' });
    expect(create).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/^Admin password/), GOOD_ADMIN);
    await userEvent.type(screen.getByLabelText(/^StageZero password/), GOOD_APP);
    await userEvent.clear(screen.getByLabelText(/^Backups folder/));
    await userEvent.type(screen.getByLabelText(/^Backups folder/), '/mnt/safe/backups');
    expect(create).toBeDisabled(); // still not recorded
    expect(screen.getByText('Confirm that you have recorded both passwords.')).toBeInTheDocument();
    await userEvent.click(screen.getByLabelText('I have recorded both passwords'));
    expect(create).toBeEnabled();
    await userEvent.click(create);

    expect(await screen.findByText('Container stagezero-postgres is running')).toBeInTheDocument();
    expect(sent).toEqual({ port: 5432, adminPassword: GOOD_ADMIN, appPassword: GOOD_APP, backupFolder: '/mnt/safe/backups', publishOnAllInterfaces: false });
    expect(screen.getByLabelText(/^Connection string/)).toHaveValue(connectionStringFor('stagezero-postgres', 5432, GOOD_APP));
    await waitFor(() => expect(tests).toBe(1));
    expect(await screen.findByText('Connected to PostgreSQL 18.0 on db:5432')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Switch and restart' })).toBeEnabled();
  });

  it('explains when Docker is out of reach instead of offering to create', async () => {
    server.use(
      signedIn(),
      http.get('/api/database', () => HttpResponse.json(sqlite)),
      http.get('/api/database/postgres/discover', () => HttpResponse.json({
        ...nothingFound, docker: { available: false, detail: 'StageZero can\'t see the Docker engine. Start the stack with docker-compose.provision.yml as well.' },
      })),
    );
    renderApp('/database');

    await userEvent.click(await screen.findByRole('button', { name: 'Look for PostgreSQL on this machine' }));
    await screen.findByText(/No PostgreSQL server answered/);
    await userEvent.click(screen.getByRole('radio', { name: 'Create a new container' }));

    expect(await screen.findByText(/docker-compose.provision.yml/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create the container' })).toBeDisabled();
  });

  it('shows a refused test as the server put it', async () => {
    server.use(
      signedIn(),
      http.get('/api/database', () => HttpResponse.json(sqlite)),
      http.post('/api/database/test', () => HttpResponse.json(
        { title: 'Could not connect to PostgreSQL on db:5432: Connection refused' }, { status: 400 })),
    );
    renderApp('/database');

    await userEvent.type(await screen.findByLabelText(/^Connection string/), 'Host=db;Database=x;Username=u;Password=p');
    await userEvent.click(screen.getByRole('button', { name: 'Test connection' }));

    expect(await screen.findByText('Could not connect to PostgreSQL on db:5432: Connection refused')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Switch and restart' })).toBeDisabled();
  });

  it('only shows a database the environment chose', async () => {
    server.use(
      signedIn(),
      http.get('/api/database', () => HttpResponse.json({
        ...postgres, source: 'environment', canChange: false,
      })),
    );
    renderApp('/database');

    expect(await screen.findByText('PostgreSQL stagezero on db:5432 as stagezero')).toBeInTheDocument();
    expect(screen.getByText(/Set by STAGEZERO_DB_PROVIDER/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Connection string/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Go back to SQLite' })).not.toBeInTheDocument();
  });
});

describe('the words on the page', () => {
  it('explain where the choice came from', () => {
    expect(sourceExplanation(sqlite)).toMatch(/default/i);
    expect(sourceExplanation(postgres)).toMatch(/database\.json/);
    expect(sourceExplanation({ ...postgres, source: 'environment' })).toMatch(/STAGEZERO_DB_CONNECTION/);
  });

  it('warn when a copy would land in a database that is not empty', () => {
    expect(testSummary(connected, true).tone).toBe('success');
    expect(testSummary({ ...connected, databaseExists: false }, true)).toMatchObject({ tone: 'success', text: expect.stringMatching(/create it/) });
    expect(testSummary({ ...connected, isEmpty: false }, true)).toMatchObject({ tone: 'warning' });
    expect(testSummary({ ...connected, isEmpty: false, hasStageZeroSchema: true }, false)).toMatchObject({ tone: 'success' });
    expect(testSummary({ ...connected, isEmpty: false, hasStageZeroSchema: true }, true).text).toMatch(/untick/);
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

describe('waiting for the restart', () => {
  const noSleep = () => Promise.resolve();

  it('ignores the closed port and the old process, and resolves on the new database', async () => {
    const answers: (() => Promise<DatabaseStatus>)[] = [
      () => Promise.reject(new Error('refused')),
      () => Promise.resolve({ ...sqlite, restartPending: true }),
      () => Promise.resolve(postgres),
    ];

    const status = await waitForDatabase('postgres', () => {
      const next = answers.shift();
      if (!next) throw new Error('polled more than expected');
      return next();
    }, noSleep);

    expect(status).toEqual(postgres);
    expect(answers).toHaveLength(0);
  });

  it('gives up after the allotted attempts', async () => {
    let polls = 0;
    const status = await waitForDatabase('postgres', () => { polls++; return Promise.reject(new Error('refused')); }, noSleep, 5);

    expect(status).toBeNull();
    expect(polls).toBe(5);
  });

  it('does not take the old process, still winding down, for the new one', async () => {
    const status = await waitForDatabase('postgres', () => Promise.resolve({ ...postgres, restartPending: true }), noSleep, 3);

    expect(status).toBeNull();
  });
});
