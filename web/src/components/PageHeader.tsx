import type { ReactNode } from 'react';

export function PageHeader({ title, children, actions }: { title: string; children?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="sz-row-between">
      <div className="sz-page-header">
        <h1>{title}</h1>
        {children ? <p className="sz-muted sz-small">{children}</p> : null}
      </div>
      {actions ? <div className="sz-row">{actions}</div> : null}
    </div>
  );
}
