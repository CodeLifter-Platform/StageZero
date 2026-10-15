import React from 'react';

const TONES = {
  accent: 'var(--cl-accent)',
  primary: 'var(--cl-primary)',
  success: 'var(--cl-success)',
  warning: 'var(--cl-warning)',
  danger: 'var(--cl-danger)',
  muted: 'var(--cl-text-muted)',
};

// Text-safe step per tone; border/background keep the fill token. Required because
// the pill paints the hue as a 14% tint and as the label sitting on that tint.
const TEXT_TONES = {
  accent: 'var(--cl-accent-text)',
  primary: 'var(--cl-primary-text)',
  success: 'var(--cl-success-text)',
  warning: 'var(--cl-warning-text)',
  danger: 'var(--cl-danger-text)',
};

/** Pill badge for product/release status (Alpha, Beta, Stable…). */
export function Badge({ tone = 'accent', children, style, ...rest }) {
  const c = TONES[tone] || TONES.accent;
  const textColor = TEXT_TONES[tone] || c;
  const bg = tone === 'muted'
    ? 'var(--cl-bg)'
    : `color-mix(in srgb, ${c} 14%, transparent)`;
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        borderRadius: 'var(--cl-radius-full)',
        padding: '5px 12px',
        fontSize: '0.72rem',
        fontWeight: 'var(--cl-weight-semibold)',
        textTransform: 'uppercase',
        letterSpacing: 'var(--cl-tracking-wide)',
        border: `1px solid ${c}`,
        background: bg,
        color: textColor,
        fontFamily: 'var(--cl-font-sans)',
        ...style,
      }}
      {...rest}
    >
      {children}
    </span>
  );
}
