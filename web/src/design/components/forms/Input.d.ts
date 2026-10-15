import React from 'react';

/** Text input with the CodeLifter focus ring (purple glow). */
export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  /** Optional field label; wraps the input in a labelled column. */
  label?: string;
  /** Helper text below the field. Replaced by `error` when that is set. */
  hint?: string;
  /**
   * Validation message. Sets the danger border, `aria-invalid` and
   * `aria-describedby` together — never color alone.
   */
  error?: string;
}

export function Input(props: InputProps): JSX.Element;
