import React from 'react';

/** Monospace metadata tag (#docker, #prompts…). */
export function Tag({ children, style, ...rest }) {
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        fontFamily: 'var(--cl-font-mono)',
        fontSize: '0.75rem',
        padding: '4px 10px',
        borderRadius: 'var(--cl-radius-sm)',
        background: 'var(--cl-bg)',
        border: '1px solid var(--cl-border)',
        color: 'var(--cl-text-muted)',
        ...style,
      }}
      {...rest}
    >
      {children}
    </span>
  );
}
