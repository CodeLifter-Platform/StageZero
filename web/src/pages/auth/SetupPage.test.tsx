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

  it('sends only the opt-ins that were ticked, then goes to sign in', async () => {
    let sent: unknown;
    let created = false;
    server.use(
      http.get('/api/auth/session', () => HttpResponse.json(sessionInfo({
        setupRequired: !created,
        signupChoices: [
          { key: 'newsletter', label: 'Send me the CodeLifter newsletter', description: '' },
          { key: 'app-updates', label: 'Tell me about StageZero updates', description: '' },
        ],
      }))),
      http.post('/api/auth/setup', async ({ request }) => {
        sent = await request.json();
        created = true;
        return HttpResponse.json({ signup: { sent: true, message: 'Check your inbox to confirm.' } });
      }),
    );
    renderApp('/setup');

    await userEvent.type(await screen.findByLabelText(/^Admin email/), 'owner@example.com');
    await userEvent.type(screen.getByLabelText(/^Password/), 'correct horse battery');
    await userEvent.type(screen.getByLabelText(/^Confirm password/), 'correct horse battery');
    await userEvent.click(screen.getByLabelText('Tell me about StageZero updates'));
    await userEvent.click(screen.getByRole('button', { name: 'Create admin account' }));

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/login'));
    expect(sent).toEqual({ email: 'owner@example.com', password: 'correct horse battery', optIns: ['app-updates'] });
    expect(await screen.findByText('Check your inbox to confirm.')).toBeInTheDocument();
  });
});
