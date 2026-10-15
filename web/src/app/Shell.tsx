import { useState, type ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Button, Sidebar } from '../ui';
import { IpChip } from './IpChip';
import { ThemeToggle } from './ThemeToggle';
import { useSession } from './session';

const SIDEBAR_KEY = 'stagezero.sidebar';

export const NAV = [
  { id: '/', label: 'Home', icon: '⌂' },
  { id: '/ip-monitor', label: 'IP Monitor', icon: '◎' },
  { id: '/dns-config', label: 'DNS Configuration', icon: '⇄' },
  { id: '/tunnel-routes', label: 'Tunnel Routes', icon: '⑂' },
  { id: '/tunnel-settings', label: 'Tunnel Setup', icon: '⚙' },
];

/** Which nav item a path belongs to: /tunnel-routes/edit/3 is under Tunnel Routes. */
export function navFor(pathname: string): string {
  const match = NAV.filter((n) => n.id !== '/' && (pathname === n.id || pathname.startsWith(`${n.id}/`)));
  return match[0]?.id ?? '/';
}

function readCollapsed(): boolean {
  try { return localStorage.getItem(SIDEBAR_KEY) === 'collapsed'; } catch { return false; }
}

/** The signed-in frame: navigation, the public IP, the theme toggle and the account. */
export function Shell({ children }: { children: ReactNode }) {
  const { info, signOut } = useSession();
  const location = useLocation();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(readCollapsed);

  const toggleCollapsed = () => setCollapsed((c) => {
    try { localStorage.setItem(SIDEBAR_KEY, c ? 'expanded' : 'collapsed'); } catch { /* storage unavailable */ }
    return !c;
  });

  return (
    <div className="sz-shell">
      <Sidebar
        sections={[{ items: NAV }]}
        value={navFor(location.pathname)}
        onSelect={(id) => navigate(id)}
        collapsed={collapsed}
        onToggleCollapsed={toggleCollapsed}
        header={<span className="sz-topbar-title">StageZero</span>}
      />
      <div className="sz-main">
        <header className="sz-topbar">
          <IpChip />
          <span className="sz-spacer" />
          <ThemeToggle />
          <span className="sz-small sz-muted" title="Signed in as">{info?.user?.email}</span>
          <Button variant="ghost" size="sm" onClick={async () => { await signOut(); navigate('/login'); }}>Sign out</Button>
        </header>
        <main className="sz-content">{children}</main>
      </div>
    </div>
  );
}

/** The frame for the sign-in pages: a title and the theme toggle, and nothing else. */
export function AnonymousFrame({ children }: { children: ReactNode }) {
  return (
    <div className="sz-auth">
      <header className="sz-topbar">
        <span className="sz-topbar-title">StageZero</span>
        <span className="sz-spacer" />
        <ThemeToggle />
      </header>
      <main className="sz-auth-body"><div className="sz-auth-card">{children}</div></main>
    </div>
  );
}
