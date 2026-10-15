import React from 'react';

export interface SegmentedOption {
  value: string;
  label: string;
  disabled?: boolean;
}

/**
 * Segmented — one row of 2–4 mutually exclusive choices, all visible at once.
 *
 * @startingPoint section="Forms" subtitle="Compact exclusive choice row" viewport="420x80"
 */
export interface SegmentedProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Choices. Plain strings are expanded to { value, label }. */
  options: Array<string | SegmentedOption>;
  /** Currently selected value. */
  value?: string;
  onChange?: (value: string) => void;
  /** Accessible group name — required, since there is no visible legend. */
  name?: string;
  /** @default "md" */
  size?: 'sm' | 'md';
}

export function Segmented(props: SegmentedProps): JSX.Element;
