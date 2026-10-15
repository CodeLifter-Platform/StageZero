import React from 'react';

/** Surface container. `interactive` adds the hover lift + purple glow. */
export function Card({ interactive = false, glow = 'primary', children, style, ...rest }) {
  const glowColor = glow === 'accent' ? 'var(--cl-accent)' : 'var(--cl-primary)';
  const glowShadow = glow === 'accent' ? 'var(--cl-glow-accent)' : 'var(--cl-glow-primary)';
  return (
    <div
      style={{
        background: 'var(--cl-surface)',
        border: '1px solid var(--cl-border)',
        borderRadius: 'var(--cl-radius-lg)',
        padding: 'var(--cl-space-xl)',
        transition: 'all var(--cl-dur) var(--cl-ease)',
        cursor: interactive ? 'pointer' : 'default',
        ...style,
      }}
      onMouseEnter={interactive ? (e) => {
        e.currentTarget.style.borderColor = glowColor;
        e.currentTarget.style.background = 'var(--cl-surface-hover)';
        e.currentTarget.style.boxShadow = glowShadow;
        e.currentTarget.style.transform = 'translateY(-3px)';
      } : undefined}
      onMouseLeave={interactive ? (e) => {
        e.currentTarget.style.borderColor = 'var(--cl-border)';
        e.currentTarget.style.background = 'var(--cl-surface)';
        e.currentTarget.style.boxShadow = 'none';
        e.currentTarget.style.transform = 'none';
      } : undefined}
      {...rest}
    >
      {children}
    </div>
  );
}
