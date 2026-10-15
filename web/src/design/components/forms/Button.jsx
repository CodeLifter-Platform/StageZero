import React from 'react';

const VARIANTS = {
  primary: {
    background: 'var(--cl-primary)',
    color: 'var(--cl-on-primary)',
    border: 'none',
    boxShadow: 'var(--cl-glow-primary)',
  },
  accent: {
    background: 'var(--cl-accent)',
    color: 'var(--cl-on-accent)',
    border: 'none',
    boxShadow: 'var(--cl-glow-accent)',
  },
  outline: {
    background: 'transparent',
    color: 'var(--cl-primary)',
    border: '2px solid var(--cl-primary)',
  },
  ghost: {
    background: 'transparent',
    color: 'var(--cl-text)',
    border: '1px solid var(--cl-border)',
  },
};

const SIZES = {
  sm: { padding: '0.45rem 0.85rem', fontSize: '0.82rem' },
  md: { padding: 'var(--cl-ctrl-py) var(--cl-ctrl-px)', fontSize: 'var(--cl-ctrl-fs)' },
  lg: { padding: '1rem 1.6rem', fontSize: '1.05rem' },
};

/**
 * CodeLifter button. Primary/accent carry the signature glow;
 * outline/ghost are quiet. Sizing follows density tokens at `md`.
 */
export function Button({
  variant = 'primary',
  size = 'md',
  disabled = false,
  leadingIcon,
  trailingIcon,
  children,
  style,
  ...rest
}) {
  const v = VARIANTS[variant] || VARIANTS.primary;
  const s = SIZES[size] || SIZES.md;
  return (
    <button
      disabled={disabled}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: '0.5rem',
        fontFamily: 'var(--cl-font-sans)',
        fontWeight: 'var(--cl-weight-semibold)',
        borderRadius: 'var(--cl-radius-md)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.4 : 1,
        transition: 'background var(--cl-dur-fast) var(--cl-ease), box-shadow var(--cl-dur-fast) var(--cl-ease)',
        ...v,
        ...s,
        ...style,
      }}
      {...rest}
    >
      {leadingIcon}
      {children}
      {trailingIcon}
    </button>
  );
}
