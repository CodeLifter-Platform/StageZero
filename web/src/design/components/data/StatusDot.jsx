import React from 'react';

const TONES = {
  success: 'var(--cl-success)',
  warning: 'var(--cl-warning)',
  danger: 'var(--cl-danger)',
  idle: 'var(--cl-text-muted)',
  accent: 'var(--cl-accent)',
};

/** Glowing status dot + optional label (Running / Paused / Exited…). */
export function StatusDot({ tone = 'success', label, style, ...rest }) {
  const c = TONES[tone] || TONES.success;
  const dot = (
    <span style={{ width: 9, height: 9, borderRadius: '50%', background: c, boxShadow: tone === 'idle' ? 'none' : `0 0 10px ${c}`, flexShrink: 0 }} />
  );
  if (!label) return dot;
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8, fontSize: '0.9rem', color: 'var(--cl-text-muted)', ...style }} {...rest}>
      {dot}{label}
    </span>
  );
}
