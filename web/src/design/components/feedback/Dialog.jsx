import React from 'react';

/** Modal dialog with dimmed, blurred backdrop and glow-lit panel. */
export function Dialog({ open, onClose, title, children, footer, style }) {
  if (!open) return null;
  return (
    <div
      onClick={onClose}
      style={{
        position: 'fixed', inset: 0, zIndex: 100,
        background: 'rgba(0,0,0,0.62)', backdropFilter: 'blur(4px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 24,
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        style={{
          width: '100%', maxWidth: 440,
          background: 'var(--cl-surface)',
          border: '1px solid var(--cl-border)',
          borderRadius: 'var(--cl-radius-xl)',
          padding: 30,
          boxShadow: 'var(--cl-glow-primary), var(--cl-shadow-lg)',
          fontFamily: 'var(--cl-font-sans)',
          ...style,
        }}
      >
        {title ? <h3 style={{ margin: '0 0 10px', fontSize: '1.35rem', fontWeight: 'var(--cl-weight-bold)', color: 'var(--cl-text)' }}>{title}</h3> : null}
        <div style={{ color: 'var(--cl-text-muted)', lineHeight: 'var(--cl-leading-normal)', fontSize: '0.95rem' }}>{children}</div>
        {footer ? <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 12, marginTop: 26 }}>{footer}</div> : null}
      </div>
    </div>
  );
}
