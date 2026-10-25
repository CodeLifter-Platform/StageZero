import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import { useSession } from '../../app/session';
import { useToast } from '../../app/toast';
import { DatabaseSwitch } from '../../components/DatabaseSwitch';
import { Notice } from '../../components/Notice';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Button, Card, Checkbox, Input } from '../../ui';

/** Validation the server repeats; checked here so the person learns before they submit. */
export function setupProblem(email: string, password: string, confirm: string): string | null {
  if (!email.trim().includes('@')) return 'Please enter a valid email address.';
  if (password.length < 8) return 'Password must be at least 8 characters long.';
  if (password !== confirm) return 'Passwords do not match.';
  return null;
}

/** First run: one form creates the admin. Shown only while no user exists. */
export function SetupPage() {
  useDocumentTitle('Setup');
  const { info, refresh } = useSession();
  const notify = useToast();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [register, setRegister] = useState(true);
  const [choosingDatabase, setChoosingDatabase] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (info && !info.setupRequired) {
    return <Notice tone="info" title="Setup already completed" action={<Button size="sm" onClick={() => navigate('/login')}>Sign in</Button>} />;
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const problem = setupProblem(email, password, confirm);
    if (problem) { setError(problem); return; }
    setBusy(true);
    setError(null);
    try {
      await api.auth.setup(email.trim(), password, (info?.codeLifterRegistration ?? false) && register);
      notify('Admin account created. Sign in to continue.', 'success');
      await refresh();
      navigate('/login', { replace: true });
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="sz-stack">
      <Card>
        <div className="sz-stack">
          <span className="sz-kicker">Optional, before you start</span>
          <Checkbox label="Keep StageZero's data in PostgreSQL" checked={choosingDatabase} disabled={busy}
            onChange={(e) => setChoosingDatabase(e.target.checked)} />
          <span className="sz-xs sz-muted sz-indent">
            Optional. Without it, StageZero uses a SQLite file in its data directory, and you can move to PostgreSQL later from Settings.
          </span>
          {choosingDatabase ? (
            <DatabaseSwitch databases={api.setupDatabase}
              onSwitched={() => notify('StageZero is now on PostgreSQL. Create your admin account.', 'success')} />
          ) : null}
        </div>
      </Card>
      <Card>
        <form className="sz-stack" onSubmit={submit} noValidate>
          <div className="sz-stack-sm">
            <h1>Welcome</h1>
            <p className="sz-muted sz-small">Create your admin account.</p>
          </div>
          <Input label="Admin email" hint="This is your login" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} />
          <Input label="Password" hint="Minimum 8 characters" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} disabled={busy} />
          <Input label="Confirm password" type="password" autoComplete="new-password" value={confirm} onChange={(e) => setConfirm(e.target.value)} disabled={busy} />
          {info?.codeLifterRegistration ? (
            <div className="sz-stack-sm">
              <Checkbox label="Register this install with codelifter.net" checked={register} disabled={busy}
                onChange={(e) => setRegister(e.target.checked)} />
              <span className="sz-xs sz-muted sz-indent">
                Sends your admin email and StageZero&apos;s version to codelifter.net, so CodeLifter knows who runs StageZero.
                It signs you up for nothing and sends you no email. The newsletter and update emails are optional, in Settings.
              </span>
            </div>
          ) : null}
          {error ? <Notice tone="danger">{error}</Notice> : null}
          <Button type="submit" disabled={busy || !email || !password || !confirm}>{busy ? 'Creating account…' : 'Create admin account'}</Button>
        </form>
      </Card>
    </div>
  );
}
