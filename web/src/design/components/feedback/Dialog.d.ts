import React from 'react';

/** Modal dialog with dimmed, blurred backdrop and glow-lit panel. */
export interface DialogProps {
  /** Whether the dialog is shown. */
  open: boolean;
  /** Called on backdrop click (wire to your close handler). */
  onClose?: () => void;
  /** Heading text. */
  title?: string;
  /** Body content. */
  children?: React.ReactNode;
  /** Footer node — typically right-aligned action Buttons. */
  footer?: React.ReactNode;
  style?: React.CSSProperties;
}

export function Dialog(props: DialogProps): JSX.Element | null;
