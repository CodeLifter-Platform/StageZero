import { useNavigate } from 'react-router-dom';
import { useDocumentTitle } from '../components/useDocumentTitle';
import { Card } from '../ui';

const FEATURES = [
  { to: '/dns-config', glyph: '⇄', title: 'Dynamic DNS', text: "Watches your public IP and updates your Cloudflare A records when it changes, keeping each record's proxy setting." },
  { to: '/tunnel-routes', glyph: '⑂', title: 'Cloudflare Tunnel', text: 'Publish services on your network at your own hostnames, with no open ports and no certificates to manage.' },
  { to: '/tunnel-routes', glyph: '⚿', title: 'Secure', text: 'Cloudflare tokens encrypted at rest, an Access policy on every new hostname, and your home IP never shown to visitors.' },
];

export function HomePage() {
  useDocumentTitle('Home');
  const navigate = useNavigate();
  return (
    <div className="sz-page">
      <div className="sz-page-header">
        <span className="sz-kicker">StageZero</span>
        <h1 style={{ fontSize: 'var(--cl-text-h2)', fontWeight: 'var(--cl-weight-black)' }}>Welcome to StageZero</h1>
        <p className="sz-muted">
          Your home network on Cloudflare: keep your DNS records pointed at your public IP, and publish
          local services through a Cloudflare Tunnel.
        </p>
      </div>
      <div className="sz-grid">
        {FEATURES.map((f) => (
          <Card key={f.title} interactive onClick={() => navigate(f.to)} role="link" tabIndex={0}
            onKeyDown={(e) => { if (e.key === 'Enter') navigate(f.to); }}>
            <div className="sz-stack-sm">
              <span aria-hidden="true" style={{ fontSize: 'var(--cl-text-h3)', color: 'var(--cl-primary-text)' }}>{f.glyph}</span>
              <h3>{f.title}</h3>
              <p className="sz-small sz-muted">{f.text}</p>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );
}
