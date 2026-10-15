import React from 'react';

/**
 * Segmented — one row of 2–4 mutually exclusive choices. Use it where a Select
 * would hide the options behind a click and radios would cost too much vertical
 * space: an environment switcher, a density switch, a now/later choice.
 */
export function Segmented({ options = [], value, onChange, name, size = 'md', style, ...rest }) {
  const pad = size === 'sm' ? '5px 11px' : '7px 14px';
  const fs = size === 'sm' ? 'var(--cl-text-xs)' : 'var(--cl-text-sm)';
  return (
    <div
      role="radiogroup"
      aria-label={name}
      style={{
        display: 'inline-flex', gap: 4, padding: 4,
        background: 'var(--cl-bg)', border: '1px solid var(--cl-border)',
        borderRadius: 'var(--cl-radius-md)', ...style,
      }}
      {...rest}
    >
      {options.map((o) => {
        const opt = typeof o === 'string' ? { value: o, label: o } : o;
        const on = opt.value === value;
        return (
          <button
            key={opt.value}
            type="button"
            role="radio"
            aria-checked={on}
            tabIndex={on ? 0 : -1}
            disabled={opt.disabled}
            onClick={() => onChange && onChange(opt.value)}
            onKeyDown={(e) => {
              if (!onChange) return;
              const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
              if (!d) return;
              e.preventDefault();
              const list = options.map((x) => (typeof x === 'string' ? x : x.value));
              const i = list.indexOf(value);
              onChange(list[(i + d + list.length) % list.length]);
            }}
            style={{
              padding: pad, fontSize: fs, fontWeight: 'var(--cl-weight-semibold)',
              fontFamily: 'var(--cl-font-sans)', border: 'none',
              borderRadius: 'var(--cl-radius-sm)',
              cursor: opt.disabled ? 'not-allowed' : 'pointer',
              opacity: opt.disabled ? 0.4 : 1,
              background: on ? 'var(--cl-primary)' : 'transparent',
              color: on ? 'var(--cl-on-primary)' : 'var(--cl-text-muted)',
              boxShadow: on ? 'var(--cl-glow-primary)' : 'none',
              transition: 'background var(--cl-dur-fast) var(--cl-ease), color var(--cl-dur-fast) var(--cl-ease)',
            }}
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}
