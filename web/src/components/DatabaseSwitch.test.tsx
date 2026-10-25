import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { DatabaseStatus, PostgresTestResult } from '../api/types';
import { server } from '../test/server';
import { owner, renderApp, sessionInfo } from '../test/render';
import { waitForPostgres } from './DatabaseSwitch';

const sqlite: DatabaseStatus = { provider: 'sqlite', source: 'default', description: 'SQLite /app-data/stagezero.db', canSwitch: true };
const postgres: DatabaseStatus = {
  provider: 'postgres', source: 'saved', description: 'PostgreSQL stagezero on host.docker.internal:5432 as stagezero', canSwitch: false,
};
const usable: PostgresTestResult = { ok: true, problem: null, serverVersion: '18.0', tableCount: 0, hasData: false, warnings: [] };

function signedIn(status: () => DatabaseStatus) {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/settings/cloudflare', () => HttpResponse.json({
      status: { isConfigured: false, isReadable: false, accountId: null, accountName: null, tokenId: null, expiresOn: null, verifiedAt: null, updatedAt: null },
      required: [], checks: null,
    })),
    http.get('/api/settings/database', () => HttpResponse.json(status())),
  );
}

async function fillIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText(/^Host/), 'host.docker.internal');
  await user.type(screen.getByLabelText(/^Password/), 'pg-secret');
}

describe('waiting for the restart', () => {
  it('ignores errors while the app is down and stops when it answers on PostgreSQL', async () => {
    const answers: (DatabaseStatus | Error)[] = [new Error('down'), sqlite, postgres];
    const status = () => {
      const next = answers.shift() ?? new Error('asked too often');
      return next instanceof Error ? Promise.reject(next) : Promise.resolve(next);
    };

    expect(await waitForPostgres(status, { sleep: async () => {} })).toBe(true);
    expect(answers).toHaveLength(0);
  });

  it('gives up after the timeout', async () => {
    expect(await waitForPostgres(() => Promise.resolve(sqlite), { intervalMs: 10, timeoutMs: 30, sleep: async () => {} })).toBe(false);
  });
});

describe('the Settings database card', () => {
  it('only offers the switch after a passing test of exactly those details', async () => {
    signedIn(() => sqlite);
    let tested: Record<string, unknown> | undefined;
    server.use(http.post('/api/settings/database/test', async ({ request }) => {
      tested = await request.json() as Record<string, unknown>;
      return HttpResponse.json(usable);
    }));
    renderApp('/settings');

    const user = userEvent.setup();
    await fillIn(user);
    expect(screen.getByRole('button', { name: 'Copy and switch' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Test connection' }));

    expect(await screen.findByText('Connected: PostgreSQL 18.0')).toBeInTheDocument();
    expect(tested).toEqual({
      host: 'host.docker.internal', port: 5432, database: 'stagezero', username: 'stagezero', password: 'pg-secret', sslMode: 'Prefer',
    });
    expect(screen.getByRole('button', { name: 'Copy and switch' })).toBeEnabled();

    await user.type(screen.getByLabelText(/^Database/), '2'); // a changed detail needs a new test
    expect(screen.getByRole('button', { name: 'Copy and switch' })).toBeDisabled();
  });

  it('shows why a database can\'t be used', async () => {
    signedIn(() => sqlite);
    server.use(http.post('/api/settings/database/test', () => HttpResponse.json({
      ...usable, ok: false, hasData: true, problem: 'This database already has StageZero data.',
    })));
    renderApp('/settings');

    const user = userEvent.setup();
    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Test connection' }));

    expect(await screen.findByText('This database already has StageZero data.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy and switch' })).toBeDisabled();
  });

  it('switches only after confirming, then shows the new database once the app is back', async () => {
    let current = sqlite;
    signedIn(() => current);
    let switched = false;
    server.use(
      http.post('/api/settings/database/test', () => HttpResponse.json(usable)),
      http.post('/api/settings/database/switch', () => {
        switched = true;
        current = postgres;
        return HttpResponse.json({ description: postgres.description, restarting: true, copiedTables: [] });
      }),
    );
    renderApp('/settings');

    const user = userEvent.setup();
    await fillIn(user);
    await user.click(screen.getByRole('button', { name: 'Test connection' }));
    await user.click(await screen.findByRole('button', { name: 'Copy and switch' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/switching runs one way/)).toBeInTheDocument();
    expect(switched).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Copy and switch' }));

    await waitFor(() => expect(screen.getByText(postgres.description)).toBeInTheDocument(), { timeout: 5000 });
    expect(switched).toBe(true);
    expect(screen.queryByRole('button', { name: 'Test connection' })).not.toBeInTheDocument();
  });

  it('says a database set in the environment can\'t be changed here', async () => {
    signedIn(() => ({ ...postgres, source: 'environment' }));
    renderApp('/settings');

    expect(await screen.findByText(/Set in the environment/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/^Host/)).not.toBeInTheDocument();
  });
});

describe('the first-run database option', () => {
  it('uses the setup calls and stays out of the way until ticked', async () => {
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ setupRequired: true }))),
      http.get('/api/setup/database', () => HttpResponse.json(sqlite)),
    );
    renderApp('/setup');

    const user = userEvent.setup();
    const option = await screen.findByLabelText("Keep StageZero's data in PostgreSQL");
    expect(screen.queryByLabelText(/^Host/)).not.toBeInTheDocument();
    await user.click(option);

    expect(await screen.findByLabelText(/^Host/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Create admin account' })).toBeInTheDocument();
  });
});
