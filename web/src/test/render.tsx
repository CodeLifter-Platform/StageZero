import { render } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { AppRoutes } from '../App';
import { SessionProvider } from '../app/session';
import { ToastProvider } from '../app/toast';
import type { SessionInfo, SessionUser } from '../api/types';

export const owner: SessionUser = {
  email: 'owner@example.com',
  requiresPasswordChange: false,
  requiresEmailVerification: false,
  accountSetupRequired: false,
};

export function sessionInfo(patch: Partial<SessionInfo> = {}): SessionInfo {
  return {
    setupRequired: false,
    user: null,
    codeLifterRegistration: false,
    passwordResetCodeHeading: 'STAGEZERO PASSWORD RESET CODE',
    verificationCodeHeading: 'STAGEZERO EMAIL VERIFICATION CODE',
    ...patch,
  };
}

/** Exposes the current location so a test can assert where navigation landed. */
function Where() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

/** The whole app (providers + routes) at a path, against whatever MSW handlers the test set. */
export function renderApp(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ToastProvider>
        <SessionProvider>
          <AppRoutes />
          <Where />
        </SessionProvider>
      </ToastProvider>
    </MemoryRouter>,
  );
}
