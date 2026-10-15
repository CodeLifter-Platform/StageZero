import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ToastProvider } from '../../app/toast';
import { ServiceTokenDialog } from './ServiceTokenDialog';

const token = { name: 'app-token', clientId: 'client-id.access', clientSecret: 's3cr3t-only-once', expiresAt: null };

function open(onDone = vi.fn()) {
  render(<ToastProvider><ServiceTokenDialog token={token} hostname="app.example.com" onDone={onDone} /></ToastProvider>);
  return onDone;
}

describe('the one-time service token dialog', () => {
  it('cannot be closed until the person says the secret is saved', async () => {
    const onDone = open();
    const done = screen.getByRole('button', { name: 'Done' });

    expect(done).toBeDisabled();
    // The backdrop is not a way out: losing this dialog loses the only copy.
    const backdrop = screen.getByRole('dialog').parentElement;
    if (backdrop) await userEvent.click(backdrop);
    expect(onDone).not.toHaveBeenCalled();

    await userEvent.click(screen.getByLabelText('I have saved the client secret'));
    await userEvent.click(done);
    expect(onDone).toHaveBeenCalledOnce();
  });

  it('masks the secret until asked, and says copying failed rather than failing silently', async () => {
    open();
    const secret = screen.getByLabelText(/CF-Access-Client-Secret/);
    expect(secret).toHaveAttribute('type', 'password');

    await userEvent.click(screen.getByRole('button', { name: 'Show' }));
    expect(secret).toHaveAttribute('type', 'text');

    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: () => Promise.reject(new Error('denied')) } });
    await userEvent.click(screen.getByRole('button', { name: 'Copy secret' }));
    expect(await screen.findByText(/Select the value and copy it manually/)).toBeInTheDocument();
  });
});
