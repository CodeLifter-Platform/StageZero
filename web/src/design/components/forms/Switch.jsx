import React from 'react';

/** Controlled toggle switch. Track fills purple when on. */
export function Switch({ checked = false, onChange, disabled = false, style }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => !disabled && onChange && onChange(!checked)}
      style={{
        width: 48,
        height: 27,
        borderRadius: 'var(--cl-radius-full)',
        border: 'none',
        padding: 3,
        display: 'flex',
        justifyContent: checked ? 'flex-end' : 'flex-start',
        alignItems: 'center',
        background: checked ? 'var(--cl-primary)' : 'var(--cl-border)',
        boxShadow: checked ? 'var(--cl-glow-primary)' : 'none',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.5 : 1,
        transition: 'background var(--cl-dur-fast) var(--cl-ease)',
        ...style,
      }}
    >
      <span style={{ width: 21, height: 21, borderRadius: '50%', background: '#fff', boxShadow: '0 1px 3px rgba(0,0,0,0.4)' }} />
    </button>
  );
}
