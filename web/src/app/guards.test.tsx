import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { server } from '../test/server';
import { owner, renderApp, sessionInfo } from '../test/render';
import { safeReturnUrl } from './guards';

const location = () => screen.getByTestId('location').textContent;

describe('route guards', () => {
  it('sends a fresh install to setup', async () => {
    server.use(http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ setupRequired: true }))));

    renderApp('/dns-config');

    await waitFor(() => expect(location()).toBe('/setup'));
    expect(await screen.findByText('Create your admin account.')).toBeInTheDocument();
  });

  it('sends a signed-out visitor to sign in, remembering where they were going, and never asks for the IP', async () => {
    let ipAsked = false;
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo())),
      http.get('/api/ip/*', () => { ipAsked = true; return HttpResponse.json({}); }),
    );

    renderApp('/tunnel-routes/edit/3');

    await waitFor(() => expect(location()).toBe('/login?returnUrl=%2Ftunnel-routes%2Fedit%2F3'));
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
    expect(ipAsked).toBe(false);
  });

  it('holds a user who owes account setup on that page', async () => {
    server.use(http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({
      user: { ...owner, requiresEmailVerification: true, accountSetupRequired: true },
    }))));

    renderApp('/dns-config');

    await waitFor(() => expect(location()).toBe('/account-setup'));
    expect(await screen.findByRole('button', { name: 'Send verification code' })).toBeInTheDocument();
  });

  it('says so when the server cannot be reached', async () => {
    server.use(http.get('/api/auth/session', () => HttpResponse.error()));

    renderApp('/');

    expect(await screen.findByText("Can't reach StageZero")).toBeInTheDocument();
  });
});

describe('safeReturnUrl', () => {
  it.each([
    ['/tunnel-routes', '/tunnel-routes'],
    [null, '/'],
    ['https://evil.example', '/'],
    ['//evil.example', '/'],
    ['/\\evil.example', '/'],
  ])('%s → %s', (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });
});
