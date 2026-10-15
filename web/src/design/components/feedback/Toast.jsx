import React from 'react';

const TONES = {
  info: 'var(--cl-accent)',
  success: 'var(--cl-success)',
  error: 'var(--cl-danger)',
  warning: 'var(--cl-warning)',
};

/** Bottom-center toast with a colored accent edge + glow. */
export function Toast({ tone = 'info', children, style }) {
  const c = TONES[tone] || TONES.info;
  return (
    <div
      role="status"
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 14,
        background: 'var(--cl-surface)',
        border: '1px solid var(--cl-border)',
        borderLeft: `3px solid ${c}`,
        borderRadius: 'var(--cl-radius-md)',
        padding: '14px 20px',
        boxShadow: `var(--cl-shadow-lg), 0 0 24px ${c}`,
        color: 'var(--cl-text)',
        fontFamily: 'var(--cl-font-sans)',
        fontSize: '0.92rem',
        ...style,
      }}
    >
      <span style={{ width: 9, height: 9, borderRadius: '50%', background: c, boxShadow: `0 0 10px ${c}`, flexShrink: 0 }} />
      {children}
    </div>
  );
}
