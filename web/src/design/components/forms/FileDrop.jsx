import React from 'react';

/** "4.2 MB", binary steps, the same rule FileBrowser uses for sizes. */
export function formatBytes(bytes) {
  if (bytes == null || isNaN(bytes)) return '';
  const u = ['B', 'KB', 'MB', 'GB', 'TB'];
  let n = Number(bytes), i = 0;
  while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
  return (i === 0 || n >= 10 ? Math.round(n) : n.toFixed(1)) + ' ' + u[i];
}

/**
 * Whether a file matches an `accept` string, the way the browser's file picker reads it:
 * extensions (".png"), exact types ("image/png") and wildcards ("image/*"), comma-separated.
 * An empty `accept` matches everything.
 */
export function acceptsFile(file, accept) {
  if (!accept || !accept.trim()) return true;
  const name = (file.name || '').toLowerCase();
  const type = (file.type || '').toLowerCase();
  return accept.split(',').map((s) => s.trim().toLowerCase()).filter(Boolean).some((rule) => {
    if (rule.startsWith('.')) return name.endsWith(rule);
    if (rule.endsWith('/*')) return type.startsWith(rule.slice(0, -1));
    return type === rule;
  });
}

/**
 * Splits dropped or picked files into the ones to hand over and the ones to refuse, with a
 * reason each: `type` (not in `accept`), `size` (over `maxSize`), `count` (a second file when
 * `multiple` is false). The first file always wins the count check.
 */
export function filterFiles(files, { accept, maxSize, multiple = true } = {}) {
  const accepted = [], rejected = [];
  Array.from(files || []).forEach((file) => {
    if (!acceptsFile(file, accept)) rejected.push({ file, reason: 'type' });
    else if (maxSize != null && file.size > maxSize) rejected.push({ file, reason: 'size' });
    else if (!multiple && accepted.length > 0) rejected.push({ file, reason: 'count' });
    else accepted.push(file);
  });
  return { accepted, rejected };
}

/** One sentence per refusal, for a hint or a toast: "photo.heic is not an accepted type". */
export function describeRejections(rejected, { accept, maxSize } = {}) {
  return rejected.map(({ file, reason }) => {
    if (reason === 'type') return `${file.name} is not an accepted type` + (accept ? ` (${accept})` : '') + '.';
    if (reason === 'size') return `${file.name} is ${formatBytes(file.size)}, over the ${formatBytes(maxSize)} limit.`;
    return `${file.name} was left out: only one file can be chosen.`;
  });
}

const UploadGlyph = ({ active }) => (
  <svg width="28" height="28" viewBox="0 0 28 28" aria-hidden="true" style={{ display: 'block', flexShrink: 0 }}>
    <path d="M6 19.5v2A2.5 2.5 0 0 0 8.5 24h11a2.5 2.5 0 0 0 2.5-2.5v-2" fill="none"
      stroke={active ? 'var(--cl-primary-text)' : 'var(--cl-text-muted)'} strokeWidth="1.6" strokeLinecap="round" />
    <path d="M14 18V5M9.5 9.5 14 5l4.5 4.5" fill="none"
      stroke={active ? 'var(--cl-primary-text)' : 'var(--cl-text-muted)'} strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

const PADDING = { sm: '14px 16px', md: '28px 20px' };

/**
 * FileDrop — a dashed zone that takes files by drag and drop, or opens the system picker on
 * click, Enter or Space. It hands the accepted `File`s to `onFiles` and never uploads
 * anything itself: the host owns the transfer, and shows its progress beside the zone.
 */
export function FileDrop({
  onFiles, onReject, accept, multiple = true, maxSize, disabled = false, busy = false,
  label, hint, error, size = 'md', name, id, style, children, ...rest
}) {
  const input = React.useRef(null);
  const depth = React.useRef(0);
  const [over, setOver] = React.useState(false);
  const [focus, setFocus] = React.useState(false);
  const reactId = React.useId();
  const zoneId = id || `cl-filedrop-${reactId}`;
  const msgId = (error || hint) ? `${zoneId}-msg` : undefined;
  const inert = disabled || busy;

  const take = (list) => {
    if (inert) return;
    const { accepted, rejected } = filterFiles(list, { accept, maxSize, multiple });
    if (rejected.length && onReject) onReject(rejected);
    if (accepted.length && onFiles) onFiles(accepted);
  };

  const open = () => { if (!inert && input.current) input.current.click(); };

  // dragenter/dragleave fire for every child; a depth counter keeps the active state steady.
  const onDragEnter = (e) => { e.preventDefault(); if (inert) return; depth.current += 1; setOver(true); };
  const onDragOver = (e) => { e.preventDefault(); if (e.dataTransfer) e.dataTransfer.dropEffect = inert ? 'none' : 'copy'; };
  const onDragLeave = (e) => { e.preventDefault(); depth.current = Math.max(0, depth.current - 1); if (depth.current === 0) setOver(false); };
  const onDrop = (e) => {
    e.preventDefault();
    depth.current = 0;
    setOver(false);
    take(e.dataTransfer ? e.dataTransfer.files : []);
  };

  const active = over && !inert;
  const border = error ? 'var(--cl-danger)' : active ? 'var(--cl-primary)' : 'var(--cl-border-strong)';
  const ring = focus ? 'var(--cl-ring)' : active ? 'var(--cl-glow-primary)' : 'none';
  const defaultLabel = multiple ? 'Drop files here, or browse' : 'Drop a file here, or browse';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 7, ...style }} {...rest}>
      <div
        id={zoneId}
        role="button"
        tabIndex={inert ? -1 : 0}
        aria-disabled={inert || undefined}
        aria-busy={busy || undefined}
        aria-invalid={error ? 'true' : undefined}
        aria-describedby={msgId}
        aria-label={typeof label === 'string' ? label : undefined}
        data-active={active || undefined}
        onClick={open}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); } }}
        onFocus={() => setFocus(true)}
        onBlur={() => setFocus(false)}
        onDragEnter={onDragEnter}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        style={{
          position: 'relative',
          display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 8,
          padding: PADDING[size] || PADDING.md,
          textAlign: 'center',
          background: active ? 'var(--cl-primary-glow)' : 'var(--cl-bg)',
          border: `2px dashed ${border}`,
          borderRadius: 'var(--cl-radius-lg)',
          color: 'var(--cl-text)',
          fontFamily: 'var(--cl-font-sans)',
          fontSize: 'var(--cl-ctrl-fs)',
          cursor: inert ? 'not-allowed' : 'pointer',
          opacity: disabled ? 0.5 : busy ? 0.7 : 1,
          outline: 'none',
          boxShadow: ring,
          transition: 'border-color var(--cl-dur-fast) var(--cl-ease), background var(--cl-dur-fast) var(--cl-ease), box-shadow var(--cl-dur-fast) var(--cl-ease)',
        }}
      >
        <UploadGlyph active={active} />
        <span style={{ fontWeight: 'var(--cl-weight-medium)', color: active ? 'var(--cl-primary-text)' : 'var(--cl-text)' }}>
          {busy ? 'Uploading…' : label || defaultLabel}
        </span>
        {children}
        <input
          ref={input}
          type="file"
          name={name}
          accept={accept}
          multiple={multiple}
          disabled={inert}
          tabIndex={-1}
          aria-hidden="true"
          style={{ position: 'absolute', width: 1, height: 1, opacity: 0, overflow: 'hidden', pointerEvents: 'none' }}
          onChange={(e) => { take(e.target.files); e.target.value = ''; }}
        />
      </div>
      {error || hint ? (
        <span id={msgId} role={error ? 'alert' : undefined}
          style={{ fontSize: 'var(--cl-text-xs)', color: error ? 'var(--cl-danger-text)' : 'var(--cl-text-muted)' }}>
          {error || hint}
        </span>
      ) : null}
    </div>
  );
}
