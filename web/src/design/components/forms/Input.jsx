import React from 'react';

/** Text input with the CodeLifter focus ring (purple glow). */
export function Input({ label, hint, error, style, id, ...rest }) {
  const inputId = id || (label ? `cl-in-${label.replace(/\s+/g, '-').toLowerCase()}` : undefined);
  const msgId = (error || hint) && inputId ? `${inputId}-msg` : undefined;
  const field = (
    <input
      id={inputId}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={msgId}
      style={{
        width: '100%',
        padding: 'var(--cl-ctrl-py) 14px',
        background: 'var(--cl-bg)',
        border: `1px solid ${error ? 'var(--cl-danger)' : 'var(--cl-border)'}`,
        borderRadius: 'var(--cl-radius-md)',
        color: 'var(--cl-text)',
        fontSize: 'var(--cl-ctrl-fs)',
        fontFamily: 'var(--cl-font-sans)',
        outline: 'none',
        boxShadow: error ? '0 0 0 3px color-mix(in srgb, var(--cl-danger) 18%, transparent)' : 'none',
        transition: 'border-color var(--cl-dur-fast) var(--cl-ease), box-shadow var(--cl-dur-fast) var(--cl-ease)',
        ...style,
      }}
      onFocus={(e) => {
        e.target.style.borderColor = 'var(--cl-primary)';
        e.target.style.boxShadow = 'var(--cl-ring)';
      }}
      onBlur={(e) => {
        e.target.style.borderColor = error ? 'var(--cl-danger)' : 'var(--cl-border)';
        e.target.style.boxShadow = error
          ? '0 0 0 3px color-mix(in srgb, var(--cl-danger) 18%, transparent)'
          : 'none';
      }}
      {...rest}
    />
  );
  if (!label && !hint && !error) return field;
  return (
    <label
      htmlFor={inputId}
      style={{
        display: 'flex', flexDirection: 'column', gap: 7,
        fontSize: 'var(--cl-text-sm)', fontWeight: 'var(--cl-weight-medium)',
        color: error ? 'var(--cl-danger-text)' : 'var(--cl-text-muted)',
      }}
    >
      {label}
      {field}
      {error || hint ? (
        <span id={msgId} style={{ fontSize: 'var(--cl-text-xs)', color: error ? 'var(--cl-danger-text)' : 'inherit' }}>
          {error || hint}
        </span>
      ) : null}
    </label>
  );
}
