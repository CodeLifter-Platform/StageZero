import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, onSessionLost } from '../api/client';
import type { SessionInfo, SessionUser } from '../api/types';

interface SessionState {
  /** Null until the first /api/auth/session answer. */
  info: SessionInfo | null;
  /** Couldn't reach the server for the first answer. */
  unreachable: boolean;
  refresh: () => Promise<SessionInfo | null>;
  setUser: (user: SessionUser | null) => void;
  signOut: () => Promise<void>;
}

const SessionContext = createContext<SessionState | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [info, setInfo] = useState<SessionInfo | null>(null);
  const [unreachable, setUnreachable] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const next = await api.auth.session();
      setInfo(next);
      setUnreachable(false);
      return next;
    } catch {
      setUnreachable(true);
      return null;
    }
  }, []);

  useEffect(() => { void refresh(); }, [refresh]);

  // A 401 or 403 from any screen means the session ended or is blocked: re-read it, and the
  // route guards send the user where they need to be.
  useEffect(() => onSessionLost(() => { void refresh(); }), [refresh]);

  const setUser = useCallback((user: SessionUser | null) => {
    setInfo((current) => (current ? { ...current, user, setupRequired: current.setupRequired && !user } : current));
  }, []);

  const signOut = useCallback(async () => {
    try { await api.auth.logout(); } finally { setUser(null); }
  }, [setUser]);

  const value = useMemo(() => ({ info, unreachable, refresh, setUser, signOut }), [info, unreachable, refresh, setUser, signOut]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionState {
  const value = useContext(SessionContext);
  if (!value) throw new Error('useSession outside SessionProvider');
  return value;
}
