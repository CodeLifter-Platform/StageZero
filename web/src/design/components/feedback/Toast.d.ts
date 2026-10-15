import React from 'react';

/** Toast notification with a colored accent edge + glow. */
export interface ToastProps {
  /** Intent — drives the edge color, dot, and glow. @default "info" */
  tone?: 'info' | 'success' | 'error' | 'warning';
  children?: React.ReactNode;
  style?: React.CSSProperties;
}

export function Toast(props: ToastProps): JSX.Element;
