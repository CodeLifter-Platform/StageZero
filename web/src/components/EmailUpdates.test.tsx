import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { EmailUpdates } from '../api/types';
import { server } from '../test/server';
import { owner, renderApp, sessionInfo } from '../test/render';

const offered: EmailUpdates = {
  enabled: true,
  email: 'owner@example.com',
  choices: [
    { key: 'newsletter', label: 'The CodeLifter newsletter', description: 'Occasional posts.' },
    { key: 'app-updates', label: 'StageZero updates', description: 'New releases.' },
  ],
};

function signedIn(view: EmailUpdates) {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/settings/cloudflare', () => HttpResponse.json({
      status: { isConfigured: false, isReadable: false, accountId: null, accountName: null, tokenId: null, expiresOn: null, verifiedAt: null, updatedAt: null },
      required: [], checks: null,
    })),
    http.get('/api/settings/database', () => HttpResponse.json(
      { provider: 'sqlite', source: 'default', description: 'SQLite', canSwitch: false })),
    http.get('/api/settings/email-updates', () => HttpResponse.json(view)),
  );
}

describe('Settings → Email from CodeLifter', () => {
  it('subscribes to each option on its own, for the signed-in address', async () => {
    signedIn(offered);
    const sent: unknown[] = [];
    server.use(http.post('/api/settings/email-updates', async ({ request }) => {
      sent.push(await request.json());
      return HttpResponse.json({ sent: true, message: 'Check owner@example.com to confirm StageZero updates.' });
    }));
    renderApp('/settings');

    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: 'Subscribe to StageZero updates' }));

    await waitFor(() => expect(sent).toEqual([{ key: 'app-updates' }]));
    expect(await screen.findByText('Check owner@example.com to confirm StageZero updates.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Subscribe to The CodeLifter newsletter' })).toBeEnabled();
    expect(screen.getByText('owner@example.com', { selector: 'strong' })).toBeInTheDocument();
  });

  it("shows the site's answer when nothing was sent", async () => {
    signedIn(offered);
    server.use(http.post('/api/settings/email-updates', () =>
      HttpResponse.json({ sent: false, message: "You're already subscribed. Nothing to confirm." })));
    renderApp('/settings');

    await userEvent.click(await screen.findByRole('button', { name: 'Subscribe to The CodeLifter newsletter' }));

    expect(await screen.findByText("You're already subscribed. Nothing to confirm.")).toBeInTheDocument();
  });

  it('says when codelifter.net is switched off on this install', async () => {
    signedIn({ enabled: false, email: 'owner@example.com', choices: [] });
    renderApp('/settings');

    const card = (await screen.findByRole('heading', { name: 'Email from CodeLifter' })).closest('div');
    expect(within(card as HTMLElement).getByText(/Switched off on this install/)).toBeInTheDocument();
  });
});
