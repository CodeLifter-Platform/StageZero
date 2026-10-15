import React from 'react';

/** Pill badge for product/release status (Alpha, Beta, Stable…). */
export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Color intent. @default "accent" */
  tone?: 'accent' | 'primary' | 'success' | 'warning' | 'danger' | 'muted';
  children?: React.ReactNode;
}

export function Badge(props: BadgeProps): JSX.Element;
