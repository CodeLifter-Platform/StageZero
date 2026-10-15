import React from 'react';

const KEYFRAMES = `
@keyframes cl-progress-slide { 0% { transform: translateX(-100%); } 100% { transform: translateX(250%); } }
@keyframes cl-progress-spin  { to { transform: rotate(360deg); } }
@keyframes cl-progress-fade  { 0%,100% { opacity: .35; } 50% { opacity: 1; } }
@media (prefers-reduced-motion: reduce) {
  [data-cl-progress-indet] { animation: cl-progress-fade 1.6s ease-in-out infinite !important; transform: none !important; }
}`;

export const TONES = {
  default: { fill: 'var(--cl-primary)', glow: 'var(--cl-primary-glow)', text: 'var(--cl-text-muted)' },
  success: { fill: 'var(--cl-success)', glow: 'color-mix(in srgb, var(--cl-success) 35%, transparent)', text: 'var(--cl-success-text)' },
  warning: { fill: 'var(--cl-warning)', glow: 'color-mix(in srgb, var(--cl-warning) 35%, transparent)', text: 'var(--cl-warning-text)' },
  danger:  { fill: 'var(--cl-danger)',  glow: 'color-mix(in srgb, var(--cl-danger) 35%, transparent)',  text: 'var(--cl-danger-text)' },
};

const clamp = (v) => Math.max(0, Math.min(100, v));

const HEIGHT = { sm: 4, md: 6, lg: 10 };

/**
 * ProgressBar — linear progress, determinate or indeterminate.
 *
 * Pass `value` (0–100) for determinate. Omit it, or pass null, for
 * indeterminate: work is happening but its size is unknown. Every part other
 * than the track is optional — label, value readout, status message, metadata.
 *
 * Anatomy, top to bottom:
 *   label ……………………………… 64%
 *   ▰▰▰▰▰▰▰▰▱▱▱▱▱
 *   status message        metadata
 */
export function ProgressBar({
  value,
  label,
  status,
  meta,
  tone = 'default',
  size = 'md',
  showValue = true,
  style,
  ...rest
}) {
  const indet = value === undefined || value === null;
  const v = indet ? 0 : clamp(value);
  const t = TONES[tone] || TONES.default;
  const h = HEIGHT[size] || HEIGHT.md;
  const hasHead = label || (!indet && showValue);
  const hasFoot = status || meta;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 7, minWidth: 0, ...style }} {...rest}>
      <style>{KEYFRAMES}</style>

      {hasHead ? (
        <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 12 }}>
          {label ? (
            <span style={{ fontSize: 'var(--cl-text-sm)', fontWeight: 'var(--cl-weight-semibold)', color: 'var(--cl-text)', minWidth: 0 }}>
              {label}
            </span>
          ) : <span />}
          {!indet && showValue ? (
            <span style={{ fontFamily: 'var(--cl-font-mono)', fontSize: 'var(--cl-text-xs)', color: 'var(--cl-text-muted)', flexShrink: 0 }}>
              {Math.round(v)}%
            </span>
          ) : null}
        </div>
      ) : null}

      <div
        role="progressbar"
        aria-label={label || status || 'Progress'}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={indet ? undefined : Math.round(v)}
        aria-valuetext={indet ? status || 'In progress' : undefined}
        aria-busy={indet ? 'true' : undefined}
        style={{
          position: 'relative', height: h, borderRadius: 'var(--cl-radius-full)',
          background: 'var(--cl-border)', overflow: 'hidden',
        }}
      >
        {indet ? (
          <span
            data-cl-progress-indet=""
            style={{
              position: 'absolute', top: 0, bottom: 0, left: 0, width: '40%',
              borderRadius: 'var(--cl-radius-full)', background: t.fill,
              boxShadow: `0 0 12px ${t.glow}`,
              animation: 'cl-progress-slide 1.4s cubic-bezier(.4,0,.2,1) infinite',
            }}
          />
        ) : (
          <span
            style={{
              position: 'absolute', top: 0, bottom: 0, left: 0, width: v + '%',
              borderRadius: 'var(--cl-radius-full)', background: t.fill,
              boxShadow: v > 0 ? `0 0 12px ${t.glow}` : 'none',
              transition: 'width var(--cl-dur) var(--cl-ease), background var(--cl-dur-fast) var(--cl-ease)',
            }}
          />
        )}
      </div>

      {hasFoot ? (
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'baseline', justifyContent: 'space-between', gap: '4px 14px' }}>
          {status ? (
            <span aria-live="polite" style={{ fontSize: 'var(--cl-text-xs)', color: tone === 'default' ? 'var(--cl-text-muted)' : t.text, minWidth: 0 }}>
              {status}
            </span>
          ) : <span />}
          {meta ? (
            <span style={{ fontFamily: 'var(--cl-font-mono)', fontSize: 'var(--cl-text-xs)', color: 'var(--cl-text-muted)', flexShrink: 0, whiteSpace: 'nowrap' }}>
              {meta}
            </span>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
