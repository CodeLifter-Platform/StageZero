import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { EmailUpdateResult, EmailUpdates as EmailUpdatesView } from '../api/types';
import { Button } from '../ui';
import { Notice } from './Notice';

/**
 * Settings → Email from CodeLifter. The newsletter and StageZero updates are separate
 * options, each its own request and its own confirmation email to the signed-in address.
 * codelifter.net is double opt-in, so asking here subscribes nothing until the link is
 * clicked. "Manage email preferences" has codelifter.net email a one-hour link to a page
 * that switches each list on or off; StageZero holds no credential for it.
 */
export function EmailUpdates() {
  const [view, setView] = useState<EmailUpdatesView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [results, setResults] = useState<Record<string, EmailUpdateResult>>({});
  const [manage, setManage] = useState<EmailUpdateResult | null>(null);

  useEffect(() => {
    api.settings.emailUpdates().then(setView).catch((err: Error) => setError(err.message));
  }, []);

  const subscribe = async (key: string) => {
    setBusy(key);
    try {
      const result = await api.settings.subscribe(key);
      setResults((r) => ({ ...r, [key]: result }));
    } catch (err) {
      setResults((r) => ({ ...r, [key]: { sent: false, message: (err as Error).message } }));
    } finally {
      setBusy(null);
    }
  };

  const requestManageLink = async () => {
    setBusy('manage');
    try {
      setManage(await api.settings.manageLink());
    } catch (err) {
      setManage({ sent: false, message: (err as Error).message });
    } finally {
      setBusy(null);
    }
  };

  if (error) return <Notice tone="danger">{error}</Notice>;
  if (!view) return null;
  if (!view.enabled) {
    return <p className="sz-small sz-muted">Switched off on this install (CodeLifter__SubscriptionsUrl is blank).</p>;
  }

  return (
    <div className="sz-stack">
      <p className="sz-small sz-muted">
        Optional, and separate: pick either, both or neither. Each sends one confirmation email
        to <strong>{view.email}</strong>, and nothing arrives until you click it. Every email has an unsubscribe link.
      </p>
      {view.choices.map((choice) => {
        const result = results[choice.key];
        return (
          <div key={choice.key} className="sz-stack-sm">
            <div className="sz-row-between">
              <div className="sz-stack-sm">
                <span className="sz-strong">{choice.label}</span>
                <span className="sz-xs sz-muted">{choice.description}</span>
              </div>
              <Button variant="outline" size="sm" disabled={busy !== null} aria-label={`Subscribe to ${choice.label}`}
                onClick={() => void subscribe(choice.key)}>
                {busy === choice.key ? 'Sending…' : 'Subscribe'}
              </Button>
            </div>
            {result ? <Notice tone={result.sent ? 'success' : 'info'}>{result.message}</Notice> : null}
          </div>
        );
      })}
      <div className="sz-stack-sm">
        <div className="sz-row-between">
          <div className="sz-stack-sm">
            <span className="sz-strong">Already subscribed?</span>
            <span className="sz-xs sz-muted">
              We'll email {view.email} a link, good for an hour, to turn each list on or off.
            </span>
          </div>
          <Button variant="ghost" size="sm" disabled={busy !== null} onClick={() => void requestManageLink()}>
            {busy === 'manage' ? 'Sending…' : 'Manage email preferences'}
          </Button>
        </div>
        {manage ? <Notice tone={manage.sent ? 'success' : 'info'}>{manage.message}</Notice> : null}
      </div>
    </div>
  );
}
