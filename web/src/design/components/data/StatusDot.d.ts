import React from 'react';

/** Glowing status dot + optional label (Running / Paused / Exited…). */
export interface StatusDotProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Status intent (drives dot color + glow). @default "success" */
  tone?: 'success' | 'warning' | 'danger' | 'idle' | 'accent';
  /** Optional text label after the dot. */
  label?: string;
}

export function StatusDot(props: StatusDotProps): JSX.Element;
