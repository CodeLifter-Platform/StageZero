import type { ReactNode } from 'react';
import { Card, StatusDot } from '../ui';

export type NoticeTone = 'info' | 'success' | 'warning' | 'danger';

const DOT = { info: 'accent', success: 'success', warning: 'warning', danger: 'danger' } as const;

/**
 * An inline notice: a design-system Card with a StatusDot carrying the tone, and the text.
 * Composed from existing components because Platform-Design has no Alert/Callout yet; the
 * tone is never carried by colour alone (the role and the words say it too).
 */
export function Notice({ tone = 'info', title, children, action }: {
  tone?: NoticeTone;
  title?: ReactNode;
  children?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <Card role={tone === 'danger' || tone === 'warning' ? 'alert' : 'status'} style={{ padding: 'var(--cl-space-md) var(--cl-space-lg)' }}>
      <div style={{ display: 'flex', gap: 'var(--cl-space-sm)', alignItems: 'flex-start' }}>
        {/* A flex wrapper: StatusDot without a label is a bare inline span, which has no size on its own. */}
        <span style={{ display: 'inline-flex', paddingTop: 7 }}><StatusDot tone={DOT[tone]} /></span>
        <div className="sz-stack-sm" style={{ flex: 1, minWidth: 0 }}>
          {title ? <div className="sz-strong">{title}</div> : null}
          {children ? <div className="sz-small" style={{ color: 'var(--cl-text-muted)' }}>{children}</div> : null}
          {action ? <div className="sz-row">{action}</div> : null}
        </div>
      </div>
    </Card>
  );
}
