import React from 'react';

/** Checkbox + label row. Uses native accent-color for the brand purple check. */
export function Checkbox({ label, style, id, ...rest }) {
  const cbId = id || (label ? `cl-cb-${label.replace(/\s+/g, '-').toLowerCase()}` : undefined);
  return (
    <label htmlFor={cbId} style={{ display: 'flex', alignItems: 'center', gap: 12, fontSize: '0.95rem', color: 'var(--cl-text)', cursor: 'pointer', ...style }}>
      <input
        id={cbId}
        type="checkbox"
        style={{ width: 18, height: 18, accentColor: 'var(--cl-primary)', cursor: 'pointer' }}
        {...rest}
      />
      {label}
    </label>
  );
}
