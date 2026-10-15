import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api/client';
import { useSession } from '../../app/session';
import { Notice } from '../../components/Notice';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Button, Card, Input } from '../../ui';

export function LoginPage() {
  useDocumentTitle('Sign in');
  const { setUser } = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      // The page's guard sends a signed-in user on: to account setup, or to returnUrl.
      setUser(await api.auth.login(email.trim(), password));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <form className="sz-stack" onSubmit={submit} noValidate>
        <h1>Sign in</h1>
        <Input label="Email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} required disabled={busy} />
        <Input label="Password" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required disabled={busy} />
        {error ? <Notice tone="danger">{error}</Notice> : null}
        <Button type="submit" disabled={busy || !email.trim() || !password}>{busy ? 'Signing in…' : 'Sign in'}</Button>
        <Link className="sz-small" to="/forgot-password">Forgot password?</Link>
      </form>
    </Card>
  );
}
