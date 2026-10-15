import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../../api/client';
import { useSession } from '../../app/session';
import { Notice } from '../../components/Notice';
import { useDocumentTitle } from '../../components/useDocumentTitle';
import { Button, Card, Input } from '../../ui';

/** StageZero sends no email: the code goes to the server log, and the page says where. */
export function ForgotPasswordPage() {
  useDocumentTitle('Forgot password');
  const { info } = useSession();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api.auth.forgotPassword(email.trim());
      setSent(true);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card>
      <form className="sz-stack" onSubmit={submit} noValidate>
        <h1>Forgot password</h1>
        <p className="sz-muted sz-small">
          StageZero doesn't send email. A 6-digit reset code is written to the server log instead;
          whoever can read the log controls the server.
        </p>
        <Input label="Email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} disabled={busy} />
        {sent ? (
          <Notice tone="success" title="Code written to the server log">
            Look for the banner headed <strong className="sz-mono">{info?.passwordResetCodeHeading ?? 'PASSWORD RESET CODE'}</strong>{' '}
            (for example <code>docker logs stagezero</code>). It expires in 15 minutes.
          </Notice>
        ) : null}
        {error ? <Notice tone="danger">{error}</Notice> : null}
        <div className="sz-row">
          <Button type="submit" variant={sent ? 'ghost' : 'primary'} disabled={busy || !email.trim()}>
            {busy ? 'Sending…' : sent ? 'Send another code' : 'Send reset code'}
          </Button>
          {sent ? <Button type="button" onClick={() => navigate(`/reset-password?email=${encodeURIComponent(email.trim())}`)}>Enter the code</Button> : null}
        </div>
        <Link className="sz-small" to="/login">Back to sign in</Link>
      </form>
    </Card>
  );
}
