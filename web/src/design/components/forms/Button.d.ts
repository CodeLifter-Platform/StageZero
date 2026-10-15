import React from 'react';

/**
 * CodeLifter button. Primary/accent carry the signature glow;
 * outline/ghost are quiet. Sizing follows density tokens at `md`.
 *
 * @startingPoint section="Forms" subtitle="Glow-lit action button" viewport="700x220"
 */
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Visual treatment. @default "primary" */
  variant?: 'primary' | 'accent' | 'outline' | 'ghost';
  /** Control size. `md` inherits the density tokens. @default "md" */
  size?: 'sm' | 'md' | 'lg';
  disabled?: boolean;
  /** Icon node rendered before the label. */
  leadingIcon?: React.ReactNode;
  /** Icon node rendered after the label. */
  trailingIcon?: React.ReactNode;
  children?: React.ReactNode;
}

export function Button(props: ButtonProps): JSX.Element;
