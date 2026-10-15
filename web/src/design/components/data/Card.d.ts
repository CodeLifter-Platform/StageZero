import React from 'react';

/** Surface container. `interactive` adds the hover lift + colored glow. */
export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Enables hover lift + glow + pointer cursor. @default false */
  interactive?: boolean;
  /** Glow hue on hover. @default "primary" */
  glow?: 'primary' | 'accent';
  children?: React.ReactNode;
}

export function Card(props: CardProps): JSX.Element;
