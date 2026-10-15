import React from 'react';

/**
 * Textarea — the multiline sibling of Input, with the same focus ring and error contract.
 *
 * @startingPoint section="Forms" subtitle="Multiline text field" viewport="420x150"
 */
export interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  /** Optional field label; wraps the field in a labelled column. */
  label?: string;
  /** Helper text below the field. Replaced by `error` when that is set. */
  hint?: string;
  /**
   * Validation message. Sets the danger border, `aria-invalid` and
   * `aria-describedby` together — never color alone.
   */
  error?: string;
  /** Render the value in JetBrains Mono — for logs, steps to reproduce, config. */
  mono?: boolean;
}

export function Textarea(props: TextareaProps): JSX.Element;
