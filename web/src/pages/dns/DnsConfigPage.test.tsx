import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { CloudflareCredential, DnsProvider } from '../../api/types';
import { NAV } from '../../app/Shell';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';

const home: DnsProvider = {
  id: 1, name: 'home.example', providerType: 'Cloudflare', zoneId: 'z-home', isActive: true,
  createdAt: '2026-10-01T00:00:00Z', lastUsedAt: null, records: [],
};

function rowOf(cell: HTMLElement): HTMLElement {
  const row = cell.closest('tr');
  if (!row) throw new Error('not in a table row');
  return row;
}

function credential(isConfigured: boolean): CloudflareCredential {
  return {
    status: {
      isConfigured, isReadable: isConfigured, accountId: null, accountName: null, tokenId: null,
      expiresOn: null, verifiedAt: null, updatedAt: null,
    },
    required: [],
    checks: null,
  };
}

function signedIn(providers: DnsProvider[], hasToken = true) {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/dns/providers', () => HttpResponse.json(providers)),
    http.get('/api/settings/cloudflare', () => HttpResponse.json(credential(hasToken))),
    http.get('/api/settings/email-updates', () => HttpResponse.json({ enabled: false, email: null, choices: [] })),
    http.get('/api/settings/database', () => HttpResponse.json(
      { provider: 'sqlite', source: 'default', description: 'SQLite /app-data/stagezero.db', canSwitch: true })),
    http.get('/api/dns/zones', () => HttpResponse.json([
      { id: 'z-home', name: 'home.example' },
      { id: 'z-lab', name: 'lab.example' },
    ])),
  );
}

describe('the DNS add-a-zone card', () => {
  it('lists the saved token\'s zones, marks the added ones, and adds a zone without sending a token', async () => {
    const providers = [home];
    signedIn(providers);
    let added: Record<string, unknown> | undefined;
    server.use(http.post('/api/dns/providers', async ({ request }) => {
      added = await request.json() as Record<string, unknown>;
      const lab = { ...home, id: 2, name: 'lab.example', zoneId: 'z-lab' };
      providers.push(lab);
      return HttpResponse.json(lab);
    }));
    renderApp('/dns-config');

    const user = userEvent.setup();
    expect(screen.queryByLabelText(/API token/)).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: 'Load zones' }));

    const zones = await screen.findByRole('table', { name: 'Zones this token can manage' });
    const homeRow = rowOf(within(zones).getByText('home.example'));
    expect(within(homeRow).getByText('Added')).toBeInTheDocument();

    const labRow = rowOf(within(zones).getByText('lab.example'));
    await user.click(within(labRow).getByRole('button', { name: 'Add provider' }));

    await waitFor(() => expect(added).toEqual({ name: 'lab.example', zoneId: 'z-lab', isActive: true }));
    expect(await within(labRow).findByText('Added')).toBeInTheDocument();
  });

  it('sends the person to Settings when no token is saved', async () => {
    signedIn([], false);
    renderApp('/dns-config');

    const user = userEvent.setup();
    expect(await screen.findByText('No Cloudflare API token saved')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load zones' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Open Settings' }));

    expect(screen.getByTestId('location').textContent).toBe('/settings');
  });
});

describe('the navigation', () => {
  it('puts Tunnel Setup before Tunnel Routes', () => {
    const labels = NAV.map((n) => n.label);
    expect(labels.indexOf('Tunnel Setup')).toBeLessThan(labels.indexOf('Tunnel Routes'));
  });
});

describe('a DNS record row', () => {
  const record = (over: Partial<DnsProvider['records'][number]>): DnsProvider['records'][number] => ({
    id: 1, providerId: 1, recordName: 'home.example', recordType: 'A', recordId: 'r1', autoUpdate: true,
    autoUpdateSupported: true, autoUpdateUnavailableReason: null, content: null, lastIpAddress: '203.0.113.7',
    lastUpdatedAt: null, ...over,
  });

  it('keeps its actions behind one menu, offers the tunnel route only for a CNAME, and confirms a delete', async () => {
    signedIn([{ ...home, records: [
      record({}),
      record({ id: 2, recordName: 'app.home.example', recordType: 'CNAME', recordId: 'r2', content: 'home.example' }),
    ] }]);
    renderApp('/dns-config');
    const user = userEvent.setup();

    const aTrigger = await screen.findByRole('button', { name: 'Actions for home.example' });
    expect(aTrigger).toHaveAttribute('aria-haspopup', 'menu');
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();

    await user.click(aTrigger);
    const menu = screen.getByRole('menu', { name: 'Actions for home.example' });
    expect(within(menu).getAllByRole('menuitem').map((i) => i.textContent)).toEqual(['Edit', 'Delete']);
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(aTrigger).toHaveFocus();

    aTrigger.focus();
    await user.keyboard('{ArrowDown}');
    expect(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Edit' })).toHaveFocus();
    await user.keyboard('{Escape}');

    await user.click(screen.getByRole('button', { name: 'Actions for app.home.example' }));
    expect(within(screen.getByRole('menu')).getAllByRole('menuitem').map((i) => i.textContent))
      .toEqual(['Edit', 'Tunnel route', 'Delete']);
    await user.click(screen.getByRole('menuitem', { name: 'Delete' }));
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    expect(within(await screen.findByRole('dialog')).getByText('Delete DNS record')).toBeInTheDocument();
  });
});
