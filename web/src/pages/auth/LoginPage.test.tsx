import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { server } from '../../test/server';
import { owner, renderApp, sessionInfo } from '../../test/render';

const location = () => screen.getByTestId('location').textContent;

async function signIn(password = 'correct horse battery') {
  await userEvent.type(await screen.findByLabelText(/^Email/), 'owner@example.com');
  await userEvent.type(screen.getByLabelText(/^Password/), password);
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

describe('sign in', () => {
  it('shows the reason a sign-in failed and stays put', async () => {
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo())),
      http.post('/api/auth/login', () => HttpResponse.json({ title: 'Invalid email or password', status: 401 }, { status: 401 })),
    );
    renderApp('/login');

    await signIn('nope');

    expect(await screen.findByText('Invalid email or password')).toBeInTheDocument();
    expect(location()).toBe('/login');
  });

  it('goes back to where the person was headed', async () => {
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo())),
      http.post('/api/auth/login', () => HttpResponse.json(owner)),
      http.get('/api/tunnel/routes', () => HttpResponse.json({ isConfigured: false, tunnelName: null, zoneName: null, routes: [] })),
      http.get('/api/ip/current', () => HttpResponse.json({ ipAddress: '203.0.113.9', lastChecked: null })),
    );
    renderApp('/login?returnUrl=%2Ftunnel-routes');

    await signIn();

    await waitFor(() => expect(location()).toBe('/tunnel-routes'));
    expect(await screen.findByText("Cloudflare Tunnel isn't set up yet")).toBeInTheDocument();
    expect(await screen.findByText('203.0.113.9')).toBeInTheDocument();
  });

  it('takes a user who owes setup straight to it', async () => {
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo())),
      http.post('/api/auth/login', () => HttpResponse.json({ ...owner, requiresPasswordChange: true, accountSetupRequired: true })),
    );
    renderApp('/login');

    await signIn();

    await waitFor(() => expect(location()).toBe('/account-setup'));
    expect(await screen.findByRole('button', { name: 'Change password' })).toBeInTheDocument();
  });
});
