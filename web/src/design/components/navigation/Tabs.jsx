import React from 'react';

/**
 * Underline tab bar. Controlled via `value` / `onChange`.
 * `tabs` is an array of { id, label }.
 */
export function Tabs({ tabs = [], value, onChange, style }) {
  return (
    <div style={{ display: 'flex', gap: 6, borderBottom: '1px solid var(--cl-border)', ...style }}>
      {tabs.map((t) => {
        const active = t.id === value;
        return (
          <button
            key={t.id}
            type="button"
            onClick={() => onChange && onChange(t.id)}
            style={{
              padding: '10px 16px',
              fontSize: '0.92rem',
              fontWeight: 'var(--cl-weight-semibold)',
              fontFamily: 'var(--cl-font-sans)',
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              marginBottom: -1,
              color: active ? 'var(--cl-text)' : 'var(--cl-text-muted)',
              borderBottom: active ? '2px solid var(--cl-primary)' : '2px solid transparent',
              transition: 'color var(--cl-dur-fast) var(--cl-ease)',
            }}
          >
            {t.label}
          </button>
        );
      })}
    </div>
  );
}
