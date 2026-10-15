import React from 'react';

/**
 * Textarea — the multiline sibling of Input. Same focus ring, same label/hint
 * column, same error contract. Vertical resize only; horizontal escapes the column.
 */
export function Textarea({ label, hint, error, mono, style, id, ...rest }) {
  const taId = id || (label ? `cl-ta-${label.replace(/\s+/g, '-').toLowerCase()}` : undefined);
  const msgId = (error || hint) && taId ? `${taId}-msg` : undefined;
  const field = (
    <textarea
      id={taId}
      rows={rest.rows || 3}
      aria-invalid={error ? 'true' : undefined}
      aria-describedby={msgId}
      style={{
        width: '100%',
        padding: 'var(--cl-ctrl-py) 14px',
        background: 'var(--cl-bg)',
        border: `1px solid ${error ? 'var(--cl-danger)' : 'var(--cl-border)'}`,
        borderRadius: 'var(--cl-radius-md)',
        color: 'var(--cl-text)',
        fontSize: mono ? 'var(--cl-text-sm)' : 'var(--cl-ctrl-fs)',
        fontFamily: mono ? 'var(--cl-font-mono)' : 'var(--cl-font-sans)',
        lineHeight: 'var(--cl-leading-normal)',
        outline: 'none',
        resize: 'vertical',
        boxSizing: 'border-box',
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
      htmlFor={taId}
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
