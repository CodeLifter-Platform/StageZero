import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import type { RouteAccess, TunnelRouteInput } from '../../api/types';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';

const defaults: RouteAccess = {
  mode: 'identity', allowedEmails: 'owner@example.com', allowedEmailDomains: null, allowedIdpIds: null,
  sessionDuration: '24h', createServiceToken: false, serviceTokenName: null, serviceTokenId: null, serviceTokenDuration: null,
};

function signedIn() {
  server.use(
    http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ user: owner }))),
    http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: null, lastChecked: null })),
    http.get('/api/tunnel/routes/defaults', () => HttpResponse.json(defaults)),
    http.get('/api/tunnel/access/identity-providers', () => HttpResponse.json([])),
    http.get('/api/tunnel/access/service-tokens', () => HttpResponse.json([])),
    http.get('/api/tunnel/routes', () => HttpResponse.json({ isConfigured: true, tunnelName: 't', zoneName: 'example.com', routes: [] })),
  );
}

describe('the route editor', () => {
  it('starts a new hostname protected for the owner, filled from a deep link', async () => {
    signedIn();
    renderApp('/tunnel-routes/add?domain=app.example.com');

    expect(await screen.findByLabelText(/^Allowed emails/)).toHaveValue('owner@example.com');
    expect(screen.getByLabelText(/^Hostname/)).toHaveValue('app.example.com');
    expect(screen.getByLabelText(/^Protection/)).toHaveValue('identity');
  });

  it('shows a newly minted secret before leaving the page', async () => {
    signedIn();
    let saved: TunnelRouteInput | undefined;
    server.use(http.post('/api/tunnel/routes', async ({ request }) => {
      saved = await request.json() as TunnelRouteInput;
      return HttpResponse.json({
        route: { ...saved, id: 7, forwardUrl: '', createdAt: '', updatedAt: '' },
        newServiceToken: { name: 'app-token', clientId: 'cid', clientSecret: 'only-once', expiresAt: null },
      });
    }));
    renderApp('/tunnel-routes/add?domain=app.example.com');

    await userEvent.selectOptions(await screen.findByLabelText(/^Protection/), 'service_token');
    await userEvent.click(screen.getByRole('switch', { name: 'Create a new token' }));
    await userEvent.type(screen.getByLabelText(/^Token name/), 'app-token');
    await userEvent.click(screen.getByRole('button', { name: 'Create route' }));

    expect(await screen.findByRole('dialog')).toHaveTextContent('Service token created');
    expect(screen.getByTestId('location').textContent).toContain('/tunnel-routes/add');
    expect(saved?.access).toMatchObject({ mode: 'service_token', createServiceToken: true, serviceTokenName: 'app-token' });

    await userEvent.click(screen.getByLabelText('I have saved the client secret'));
    await userEvent.click(screen.getByRole('button', { name: 'Done' }));
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/tunnel-routes'));
  });
});
