import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { server } from '../../test/server';
import { renderApp, sessionInfo } from '../../test/render';
import { setupProblem } from './SetupPage';

describe('first-run setup', () => {
  it('refuses an unusable form before sending it', () => {
    expect(setupProblem('nobody', 'long enough pw', 'long enough pw')).toMatch(/valid email/);
    expect(setupProblem('a@b.c', 'short', 'short')).toMatch(/at least 8/);
    expect(setupProblem('a@b.c', 'long enough pw', 'different pw!!')).toMatch(/do not match/);
    expect(setupProblem('a@b.c', 'long enough pw', 'long enough pw')).toBeNull();
  });

  it('registers with codelifter.net unless unticked, and offers no subscriptions', async () => {
    let sent: unknown;
    let created = false;
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ setupRequired: !created, codeLifterRegistration: true }))),
      http.post('/api/auth/setup', async ({ request }) => {
        sent = await request.json();
        created = true;
        return HttpResponse.json({ registeredWithCodeLifter: false });
      }),
    );
    renderApp('/setup');

    const register = await screen.findByLabelText('Register this install with codelifter.net');
    expect(register).toBeChecked();
    expect(screen.getByText(/signs you up for nothing/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/newsletter/i)).not.toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(/^Admin email/), 'owner@example.com');
    await userEvent.type(screen.getByLabelText(/^Password/), 'correct horse battery');
    await userEvent.type(screen.getByLabelText(/^Confirm password/), 'correct horse battery');
    await userEvent.click(register);
    await userEvent.click(screen.getByRole('button', { name: 'Create admin account' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/login'));
    expect(sent).toEqual({ email: 'owner@example.com', password: 'correct horse battery', registerWithCodeLifter: false });
  });

  it('offers no registration when codelifter.net is switched off', async () => {
    server.use(http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({ setupRequired: true }))));
    renderApp('/setup');

    await screen.findByLabelText(/^Admin email/);
    expect(screen.queryByLabelText('Register this install with codelifter.net')).not.toBeInTheDocument();
  });
});
