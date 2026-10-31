import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { ConnectionTest, DatabaseStatus } from '../../api/types';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';
import { sourceExplanation, testSummary, waitForDatabase } from './DatabasePage';

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
