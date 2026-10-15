import { useState, type FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import { useToast } from '../../app/toast';
import { Notice } from '../../components/Notice';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Button, Card, Input } from '../../ui';

export function ResetPasswordPage() {
  useDocumentTitle('Reset password');
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const notify = useToast();
  const [email, setEmail] = useState(params.get('email') ?? '');
  const [code, setCode] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (password.length < 8) { setError('Password must be at least 8 characters long.'); return; }
    if (password !== confirm) { setError('Passwords do not match.'); return; }
    setBusy(true);
    setError(null);
    try {
      await api.auth.resetPassword(email.trim(), code.trim(), password);
      notify("Password reset. You'll verify your email when you next sign in.", 'success');
      navigate('/login', { replace: true });
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <form className="sz-stack" onSubmit={submit} noValidate>
        <h1>Reset password</h1>
        <p className="sz-muted sz-small">Enter the 6-digit code from the server log and choose a new password.</p>
        <Input label="Email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} />
        <Input label="6-digit code" inputMode="numeric" maxLength={6} autoComplete="one-time-code" value={code} onChange={(e) => setCode(e.target.value)} disabled={busy} />
        <Input label="New password" hint="Minimum 8 characters" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} />
        <Input label="Confirm new password" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
        {error ? <Notice tone="danger">{error}</Notice> : null}
        <Button type="submit" disabled={busy || !email.trim() || !code.trim() || !password || !confirm}>{busy ? 'Resetting…' : 'Reset password'}</Button>
        <Link className="sz-small" to="/forgot-password">Back to forgot password</Link>
      </form>
    </Card>
  );
}
