import React from 'react';

/** Native select styled to match CodeLifter inputs. */
export function Select({ label, children, style, id, ...rest }) {
  const selId = id || (label ? `cl-sel-${label.replace(/\s+/g, '-').toLowerCase()}` : undefined);
  const field = (
    <select
      id={selId}
      style={{
        width: '100%',
        padding: 'var(--cl-ctrl-py) 14px',
        background: 'var(--cl-bg)',
        border: '1px solid var(--cl-border)',
        borderRadius: 'var(--cl-radius-md)',
        color: 'var(--cl-text)',
        fontSize: 'var(--cl-ctrl-fs)',
        fontFamily: 'var(--cl-font-sans)',
        outline: 'none',
        cursor: 'pointer',
        ...style,
      }}
      onFocus={(e) => { e.target.style.borderColor = 'var(--cl-primary)'; e.target.style.boxShadow = 'var(--cl-ring)'; }}
      onBlur={(e) => { e.target.style.borderColor = 'var(--cl-border)'; e.target.style.boxShadow = 'none'; }}
      {...rest}
    >
      {children}
    </select>
  );
  if (!label) return field;
  return (
    <label htmlFor={selId} style={{ display: 'flex', flexDirection: 'column', gap: 7, fontSize: 'var(--cl-text-sm)', fontWeight: 'var(--cl-weight-medium)', color: 'var(--cl-text-muted)' }}>
      {label}
      {field}
    </label>
  );
}
