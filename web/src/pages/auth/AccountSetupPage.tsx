import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import { AnonymousFrame } from '../../app/Shell';
import { useSession } from '../../app/session';
import { useToast } from '../../app/toast';
import { Loading } from '../../components/Loading';
import { Notice } from '../../components/Notice';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Button, Card, Input } from '../../ui';

/**
 * What a signed-in user owes before using the app: verifying their email (after a password
 * reset) and replacing a one-time password (after `reset-password` on the server). Leaving
 * signs them out, since nothing else is open to them until it is done.
 */
export function AccountSetupPage() {
  useDocumentTitle('Account setup');
  const { info, setUser, signOut } = useSession();
  const navigate = useNavigate();
  const notify = useToast();
  const user = info?.user;

  const [email, setEmail] = useState(user?.email ?? '');
  const [codeSent, setCodeSent] = useState(false);
  const [code, setCode] = useState('');
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!info) return <AnonymousFrame><Loading /></AnonymousFrame>;
  if (!user) return <Navigate to="/login" replace />;
  if (!user.accountSetupRequired) return <Navigate to="/" replace />;

  const run = async (work: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    try { await work(); } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  const finish = (updated: typeof user) => {
    setUser(updated);
    if (!updated.accountSetupRequired) {
      notify('Account setup complete.', 'success');
      navigate('/', { replace: true });
    }
  };

  const sendCode = (e: FormEvent) => { e.preventDefault(); void run(async () => { await api.auth.sendVerification(email.trim()); setCodeSent(true); }); };
  const verify = (e: FormEvent) => { e.preventDefault(); void run(async () => finish(await api.auth.verifyEmail(code.trim()))); };
  const change = (e: FormEvent) => {
    e.preventDefault();
    if (next.length < 8) { setError('Password must be at least 8 characters long.'); return; }
    if (next !== confirm) { setError('Passwords do not match.'); return; }
    void run(async () => finish(await api.auth.changePassword(current, next)));
  };

  const leave = async () => { await signOut(); navigate('/login', { replace: true }); };

  return (
    <AnonymousFrame>
      <Card>
        <div className="sz-stack">
          <div className="sz-stack-sm">
            <h1>Account setup</h1>
            <p className="sz-muted sz-small">
              {user.requiresEmailVerification ? 'Verify your email address' : 'Replace your one-time password'} to secure your account.
            </p>
          </div>

          {user.requiresEmailVerification ? (
            <form className="sz-stack" onSubmit={codeSent ? verify : sendCode} noValidate>
              <Input label="Email address" type="email" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy || codeSent} />
              {codeSent ? (
                <>
                  <Notice tone="success" title="Code written to the server log">
                    Look for the banner headed <strong className="sz-mono">{info.verificationCodeHeading}</strong>.
                  </Notice>
                  <Input label="Verification code (6 digits)" inputMode="numeric" maxLength={6} autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} disabled={busy} />
                  <div className="sz-row">
                    <Button type="submit" disabled={busy || code.trim().length !== 6}>{busy ? 'Verifying…' : 'Verify'}</Button>
                    <Button type="button" variant="ghost" disabled={busy} onClick={() => { setCodeSent(false); setCode(''); }}>Send a new code</Button>
                  </div>
                </>
              ) : (
                <Button type="submit" disabled={busy || !email.includes('@')}>{busy ? 'Sending…' : 'Send verification code'}</Button>
              )}
            </form>
          ) : (
            <form className="sz-stack" onSubmit={change} noValidate>
              <Input label="Current password" type="password" autoComplete="current-password" value={current} onChange={(e) => setCurrent(e.target.value)} disabled={busy} />
              <Input label="New password" hint="Minimum 8 characters" type="password" autoComplete="new-password" value={next} onChange={(e) => setNext(e.target.value)} disabled={busy} />
              <Input label="Confirm new password" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
              <Button type="submit" disabled={busy || !current || !next || !confirm}>{busy ? 'Saving…' : 'Change password'}</Button>
            </form>
          )}

          {error ? <Notice tone="danger">{error}</Notice> : null}
          <Button type="button" variant="ghost" size="sm" onClick={() => void leave()} disabled={busy}>Sign out</Button>
        </div>
      </Card>
    </AnonymousFrame>
  );
}
