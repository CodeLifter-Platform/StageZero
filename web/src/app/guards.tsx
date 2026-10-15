import type { ReactNode } from 'react';
import { Navigate, useLocation, useSearchParams } from 'react-router-dom';
import { Loading } from '../components/Loading';
import { Notice } from '../components/Notice';
import { Button } from '../ui';
import { AnonymousFrame, Shell } from './Shell';
import { useSession } from './session';

function Waiting() {
  const { unreachable, refresh } = useSession();
  return (
    <AnonymousFrame>
      {unreachable
        ? <Notice tone="danger" title="Can't reach StageZero" action={<Button size="sm" onClick={() => void refresh()}>Try again</Button>}>
            Check that the server is running.
          </Notice>
        : <Loading />}
    </AnonymousFrame>
  );
}

/** Signed in with nothing owed: the app itself, inside the shell. */
export function RequireOperator({ children }: { children: ReactNode }) {
  const { info } = useSession();
  const location = useLocation();
  if (!info) return <Waiting />;
  if (info.setupRequired) return <Navigate to="/setup" replace />;
  if (!info.user) {
    const returnUrl = location.pathname + location.search;
    return <Navigate to={returnUrl === '/' ? '/login' : `/login?returnUrl=${encodeURIComponent(returnUrl)}`} replace />;
  }
  if (info.user.accountSetupRequired) return <Navigate to="/account-setup" replace />;
  return <Shell>{children}</Shell>;
}

/**
 * The sign-in pages. A fresh install is sent to setup; a signed-in operator is sent on to
 * where they were headed (the returnUrl a guard put on /login), which is also how a
 * successful sign-in leaves the page.
 */
export function AnonymousOnly({ children, allowFreshInstall = false }: { children: ReactNode; allowFreshInstall?: boolean }) {
  const { info } = useSession();
  const [params] = useSearchParams();
  if (!info) return <Waiting />;
  if (info.setupRequired && !allowFreshInstall) return <Navigate to="/setup" replace />;
  if (info.user?.accountSetupRequired) return <Navigate to="/account-setup" replace />;
  if (info.user) return <Navigate to={safeReturnUrl(params.get('returnUrl'))} replace />;
  return <AnonymousFrame>{children}</AnonymousFrame>;
}

/** Only a returnUrl inside this app is followed, so a link can't bounce a sign-in elsewhere. */
export function safeReturnUrl(value: string | null): string {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\') ? value : '/';
}
