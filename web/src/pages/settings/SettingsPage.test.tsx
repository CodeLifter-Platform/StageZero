import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { CloudflareCredential, CloudflarePermission } from '../../api/types';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';

const required: CloudflarePermission[] = [
  { scope: 'Account', item: 'Cloudflare Tunnel', level: 'Edit', usedFor: 'Create or adopt the tunnel and push its routes' },
  { scope: 'Account', item: 'Access: Apps and Policies', level: 'Edit', usedFor: 'Put Cloudflare Access in front of tunnel routes' },
  { scope: 'Account', item: 'Access: Service Tokens', level: 'Edit', usedFor: 'Mint service tokens for machine access to routes' },
  { scope: 'Zone', item: 'DNS', level: 'Edit', usedFor: 'Keep records on your public IP and write tunnel CNAMEs' },
  { scope: 'Zone', item: 'Zone', level: 'Read', usedFor: 'List the zones to manage' },
];

const none: CloudflareCredential = {
  status: {
    isConfigured: false, isReadable: false, accountId: null, accountName: null, tokenId: null,
    expiresOn: null, verifiedAt: null, updatedAt: null,
  },
  required,
  checks: null,
};

const saved: CloudflareCredential = {
  status: {
    isConfigured: true, isReadable: true, accountId: 'f182bcea0bbe5c6e3c847985ce433132', accountName: 'Home',
    tokenId: 'token-id-1', expiresOn: null, verifiedAt: '2026-10-25T00:00:00Z', updatedAt: '2026-10-25T00:00:00Z',
  },
  required,
  checks: required.map((permission) => ({
    permission, granted: permission.item !== 'Access: Service Tokens', detail: 'seen',
  })),
};

function signedIn(initial: CloudflareCredential) {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/settings/cloudflare', () => HttpResponse.json(initial)),
    http.get('/api/settings/email-updates', () => HttpResponse.json({ enabled: false, email: null, choices: [] })),
    http.get('/api/settings/database', () => HttpResponse.json(
      { provider: 'sqlite', source: 'default', description: 'SQLite /app-data/stagezero.db', canSwitch: true })),
  );
}

describe('the settings page', () => {
  it('opens from the email in the top bar', async () => {
    signedIn(none);
    server.use(http.get('/api/dns/providers', () => HttpResponse.json([])));
    renderApp('/dns-config');

    await userEvent.click(await screen.findByRole('button', { name: /owner@example\.com/ }));

    expect(screen.getByTestId('location').textContent).toBe('/settings');
    expect(await screen.findByRole('heading', { name: 'Cloudflare API token' })).toBeInTheDocument();
  });

  it('shows every permission the one token needs before any token is saved', async () => {
    signedIn(none);
    renderApp('/settings');

    const table = await screen.findByRole('table', { name: 'Permissions the Cloudflare token needs' });
    expect(within(table).getAllByRole('row')).toHaveLength(required.length + 1);
    expect(within(table).getByText('Access: Service Tokens')).toBeInTheDocument();
    for (const p of required) expect(screen.getByText(p.usedFor)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save token' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
  });

  it('saves a token, then shows what each permission check found and forgets the typed token', async () => {
    signedIn(none);
    let body: Record<string, unknown> | undefined;
    server.use(http.put('/api/settings/cloudflare', async ({ request }) => {
      body = await request.json() as Record<string, unknown>;
      return HttpResponse.json(saved);
    }));
    renderApp('/settings');

    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/^API token/), ' cf-token ');
    await user.click(screen.getByRole('button', { name: 'Save token' }));

    await waitFor(() => expect(body).toEqual({ apiToken: 'cf-token', accountId: null }));
    const table = await screen.findByRole('table', { name: 'Permissions the Cloudflare token needs' });
    expect(within(table).getAllByText('Granted')).toHaveLength(4);
    expect(within(table).getByText('Missing')).toBeInTheDocument();
    expect(screen.getByText('f182bcea0bbe5c6e3c847985ce433132')).toBeInTheDocument();
    expect(screen.queryByLabelText(/^API token/)).not.toBeInTheDocument();
  });

  it('shows Cloudflare\'s reason when a token is refused', async () => {
    signedIn(none);
    server.use(http.put('/api/settings/cloudflare', () => HttpResponse.json(
      { title: 'This token can\'t be used until 2026-12-31 08:00 UTC.', status: 400 }, { status: 400 })));
    renderApp('/settings');

    const user = userEvent.setup();
    await user.type(await screen.findByLabelText(/^API token/), 'cf-token');
    await user.click(screen.getByRole('button', { name: 'Save token' }));

    expect(await screen.findByText(/can't be used until 2026-12-31/)).toBeInTheDocument();
  });

  it('replaces the saved token with the account id when given', async () => {
    signedIn(saved);
    let body: Record<string, unknown> | undefined;
    server.use(http.put('/api/settings/cloudflare', async ({ request }) => {
      body = await request.json() as Record<string, unknown>;
      return HttpResponse.json(saved);
    }));
    renderApp('/settings');

    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Replace token' }));
    await user.type(screen.getByLabelText(/^API token/), 'new-token');
    await user.type(screen.getByLabelText(/^Account ID/), 'acct-2');
    await user.click(screen.getByRole('button', { name: 'Replace token' }));

    await waitFor(() => expect(body).toEqual({ apiToken: 'new-token', accountId: 'acct-2' }));
  });

  it('removes the token only after confirming, and says it keeps working in Cloudflare', async () => {
    signedIn(saved);
    let removed = false;
    server.use(http.delete('/api/settings/cloudflare', () => { removed = true; return new HttpResponse(null, { status: 204 }); }));
    renderApp('/settings');

    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Remove' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/keeps working until you delete it in Cloudflare/)).toBeInTheDocument();
    expect(removed).toBe(false);
    await user.click(within(dialog).getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(removed).toBe(true));
    expect(await screen.findByRole('button', { name: 'Save token' })).toBeInTheDocument();
  });
});
