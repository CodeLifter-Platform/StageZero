import React from 'react';

/**
 * ProgressBar — linear progress, determinate or indeterminate, with optional
 * label, value readout, status message and metadata.
 *
 * @startingPoint section="Progress" subtitle="Linear progress, determinate or indeterminate" viewport="480x110"
 */
export interface ProgressBarProps extends React.HTMLAttributes<HTMLDivElement> {
  /**
   * 0–100. Omit, or pass null, for indeterminate — work is happening but its
   * size is unknown. Values outside the range are clamped.
   */
  value?: number | null;
  /** What is progressing. Shown above the track. */
  label?: React.ReactNode;
  /**
   * What is happening right now, e.g. "Receiving objects". Announced politely to
   * screen readers when it changes. Takes the tone colour for non-default tones.
   */
  status?: React.ReactNode;
  /** Quantities in mono, e.g. "774 / 1,208 MB · 38 MB/s". Right-aligned under the track. */
  meta?: React.ReactNode;
  /**
   * default = in progress; success = finished; warning = slowed or retrying;
   * danger = failed. @default "default"
   */
  tone?: 'default' | 'success' | 'warning' | 'danger';
  /** Track height: 4 / 6 / 10px. @default "md" */
  size?: 'sm' | 'md' | 'lg';
  /** Show the percentage beside the label. Ignored when indeterminate. @default true */
  showValue?: boolean;
}

export function ProgressBar(props: ProgressBarProps): JSX.Element;
