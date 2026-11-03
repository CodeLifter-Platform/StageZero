import React from 'react';
import { TABLE_DENSITY } from './DataTable.jsx';
import { ProgressBar } from '../progress/ProgressBar.jsx';
import { Button } from '../forms/Button.jsx';

/** "4.2 KB", binary steps like `ls -lh`. Folders have no size. */
export function formatSize(bytes) {
  if (bytes == null || isNaN(bytes)) return '—';
  const u = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
  let n = Number(bytes), i = 0;
  while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
  return (i === 0 || n >= 10 ? Math.round(n) : n.toFixed(1)) + ' ' + u[i];
}

/** "2026-10-20 14:03" in local time: sortable, unambiguous, the same width on every row. */
export function formatModified(v) {
  if (v == null) return '—';
  const d = v instanceof Date ? v : new Date(v);
  if (isNaN(d)) return String(v);
  const p = (x) => String(x).padStart(2, '0');
  return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
}

/** The folder `path` is in, or null at the root. Understands "/", "~" and relative roots. */
export function parentPath(path) {
  if (!path || path === '/' || path === '~') return null;
  const t = path.replace(/\/+$/, '');
  const i = t.lastIndexOf('/');
  if (i < 0) return null;
  return i === 0 ? '/' : t.slice(0, i);
}

export function joinPath(path, name) {
  return (path.endsWith('/') ? path : path + '/') + name;
}

/** Breadcrumb segments: [{ label, path }], root first. */
export function pathSegments(path) {
  const abs = path.startsWith('/');
  const out = abs ? [{ label: '/', path: '/' }] : [];
  let acc = abs ? '' : null;
  path.split('/').filter(Boolean).forEach((seg) => {
    acc = acc === null ? seg : acc + '/' + seg;
    out.push({ label: seg, path: acc });
  });
  return out;
}

/** Folders first in either direction; names compare the way people count ("log2" before "log10"). */
export function sortEntries(entries, key = 'name', dir = 'asc') {
  const byName = (a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });
  const get = key === 'name' ? null : COLS[key].sort;
  return [...entries].sort((a, b) => {
    const da = a.kind === 'dir' ? 0 : 1, db = b.kind === 'dir' ? 0 : 1;
    if (da !== db) return da - db;
    const x = get ? get(a) : 0, y = get ? get(b) : 0;
    const c = (x < y ? -1 : x > y ? 1 : 0) || byName(a, b);
    return dir === 'desc' ? -c : c;
  });
}

const Glyph = ({ kind }) => {
  const common = { width: 15, height: 15, viewBox: '0 0 16 16', 'aria-hidden': true, style: { flexShrink: 0, display: 'block' } };
  if (kind === 'dir') {
    return <svg {...common}><path d="M1.5 4.2c0-.7.6-1.2 1.2-1.2h3.4l1.5 1.6h5.7c.7 0 1.2.5 1.2 1.2v6.5c0 .7-.5 1.2-1.2 1.2H2.7c-.7 0-1.2-.5-1.2-1.2z" fill="var(--cl-primary-text)" fillOpacity="0.85" /></svg>;
  }
  const page = <path d="M3.5 1.5h6l3 3v10h-9z" fill="none" stroke="var(--cl-text-muted)" strokeWidth="1.2" strokeLinejoin="round" />;
  if (kind === 'link') {
    return <svg {...common}>{page}<path d="M6 11.5l3.5-3.5M6.8 8h2.7v2.7" fill="none" stroke="var(--cl-text-muted)" strokeWidth="1.2" strokeLinecap="round" strokeLinejoin="round" /></svg>;
  }
  return <svg {...common}>{page}<path d="M9.5 1.5v3h3" fill="none" stroke="var(--cl-text-muted)" strokeWidth="1.2" strokeLinejoin="round" /></svg>;
};

const COLS = {
  size: { label: 'Size', width: 84, numeric: true, value: (e) => (e.kind === 'dir' ? '—' : formatSize(e.size)), sort: (e) => e.size ?? -1 },
  modified: { label: 'Modified', width: 150, numeric: true, value: (e) => formatModified(e.modified), sort: (e) => (e.modified == null ? 0 : +new Date(e.modified)) },
  mode: { label: 'Mode', width: 100, mono: true, value: (e) => e.mode || '—', sort: (e) => e.mode || '' },
};

/**
 * FileBrowser — one folder of a file system you reach over a connection: a path bar, then
 * folders and files with size and date. Folders always sort first. Double-click or Enter
 * opens; Backspace goes up. Row metrics come from DataTable, so the two sit together.
 */
export function FileBrowser({
  path = '/', entries = [], onNavigate, onOpen, selected, onSelectedChange,
  columns = ['size', 'modified'], density = 'compact', showHidden = false,
  loading = false, error, onRetry, toolbar, empty = 'This folder is empty', openOnClick = false,
  maxHeight, label = 'Files', style, ...rest
}) {
  const D = TABLE_DENSITY[density] || TABLE_DENSITY.compact;
  const [sort, setSort] = React.useState({ key: 'name', dir: 'asc' });
  const [innerSel, setInnerSel] = React.useState(() => new Set());
  const [focus, setFocus] = React.useState(0);
  const [focused, setFocused] = React.useState(false);
  const anchor = React.useRef(null);
  const typed = React.useRef({ text: '', at: 0 });
  const body = React.useRef(null);
  const sel = selected ? new Set(selected) : innerSel;
  const setSel = (n) => { if (!selected) setInnerSel(n); onSelectedChange && onSelectedChange([...n]); };

  React.useEffect(() => { setInnerSel(new Set()); setFocus(0); anchor.current = null; }, [path]);

  const rows = React.useMemo(() => {
    const visible = entries.filter((e) => showHidden || !e.name.startsWith('.'));
    return sortEntries(visible, sort.key, sort.dir);
  }, [entries, showHidden, sort]);

  const parent = parentPath(path);
  const activate = (e) => {
    if (e.kind === 'dir') onNavigate && onNavigate(joinPath(path, e.name));
    else onOpen && onOpen(e);
  };
  const focusRow = (i) => {
    const j = Math.max(0, Math.min(rows.length - 1, i));
    setFocus(j);
    const tr = body.current && body.current.querySelectorAll('tr[data-row]')[j];
    if (tr) { tr.focus(); tr.scrollIntoView && tr.scrollIntoView({ block: 'nearest' }); }
    return j;
  };
  const pick = (i, ev) => {
    const name = rows[i].name;
    if (ev.shiftKey && anchor.current != null) {
      const [a, b] = [Math.min(anchor.current, i), Math.max(anchor.current, i)];
      setSel(new Set(rows.slice(a, b + 1).map((r) => r.name)));
    } else if (ev.metaKey || ev.ctrlKey) {
      const n = new Set(sel); n.has(name) ? n.delete(name) : n.add(name); setSel(n); anchor.current = i;
    } else { setSel(new Set([name])); anchor.current = i; }
    setFocus(i);
  };
  const onKey = (ev, i) => {
    if (ev.key === 'ArrowDown' || ev.key === 'ArrowUp') {
      ev.preventDefault();
      if (ev.altKey && ev.key === 'ArrowUp') { if (parent) onNavigate && onNavigate(parent); return; }
      const j = focusRow(i + (ev.key === 'ArrowDown' ? 1 : -1));
      pick(j, ev.shiftKey ? ev : { });
    } else if (ev.key === 'Home' || ev.key === 'End') {
      ev.preventDefault(); pick(focusRow(ev.key === 'Home' ? 0 : rows.length - 1), {});
    } else if (ev.key === 'Enter') { ev.preventDefault(); activate(rows[i]); }
    else if (ev.key === 'Backspace') { ev.preventDefault(); if (parent) onNavigate && onNavigate(parent); }
    else if (ev.key === ' ') { ev.preventDefault(); pick(i, { metaKey: true }); }
    else if (ev.key.toLowerCase() === 'a' && (ev.metaKey || ev.ctrlKey)) { ev.preventDefault(); setSel(new Set(rows.map((r) => r.name))); }
    else if (ev.key.length === 1 && !ev.metaKey && !ev.ctrlKey && !ev.altKey) {
      const t = typed.current, now = Date.now();
      t.text = (now - t.at > 800 ? '' : t.text) + ev.key.toLowerCase(); t.at = now;
      const j = rows.findIndex((r) => r.name.toLowerCase().startsWith(t.text));
      if (j >= 0) pick(focusRow(j), {});
    }
  };

  const toggleSort = (key) => setSort((s) => (s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' }));
  const cols = columns.filter((c) => COLS[c]);
  const head = (key, text, c) => {
    const on = sort.key === key, right = c && c.numeric;
    return (
      <th key={key} scope="col" aria-sort={on ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'}
        style={{ padding: `0 ${D.px}px`, height: D.head, width: c ? c.width : undefined, textAlign: right ? 'right' : 'left', whiteSpace: 'nowrap',
          fontFamily: 'var(--cl-font-mono)', fontSize: Math.min(11, D.mono), fontWeight: 500, letterSpacing: '0.08em', textTransform: 'uppercase',
          color: on ? 'var(--cl-text)' : 'var(--cl-text-muted)', background: 'var(--cl-bg)', borderBottom: '1px solid var(--cl-border)', position: 'sticky', top: 0, zIndex: 1 }}>
        <button type="button" onClick={() => toggleSort(key)}
          style={{ all: 'unset', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 5, flexDirection: right ? 'row-reverse' : 'row' }}>
          {text}<span aria-hidden="true" style={{ fontSize: '0.8em', opacity: on ? 1 : 0.35 }}>{on && sort.dir === 'desc' ? '▼' : '▲'}</span>
        </button>
      </th>
    );
  };
  const segs = pathSegments(path);
  const crumbBtn = { all: 'unset', cursor: 'pointer', padding: '2px 4px', borderRadius: 'var(--cl-radius-sm)' };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0, border: '1px solid var(--cl-border)', borderRadius: 'var(--cl-radius-lg)', background: 'var(--cl-surface)', overflow: 'hidden', ...style }} {...rest}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: `6px ${D.px}px`, background: 'var(--cl-bg)', borderBottom: '1px solid var(--cl-border)', minHeight: 36, boxSizing: 'border-box' }}>
        <button type="button" disabled={!parent} onClick={() => parent && onNavigate && onNavigate(parent)} aria-label="Enclosing folder" title="Enclosing folder"
          style={{ all: 'unset', boxSizing: 'border-box', width: 26, height: 26, display: 'grid', placeItems: 'center', borderRadius: 'var(--cl-radius-sm)',
            border: '1px solid var(--cl-border)', cursor: parent ? 'pointer' : 'default', color: 'var(--cl-text-muted)', opacity: parent ? 1 : 0.4, fontSize: 13 }}>
          {'↑'}
        </button>
        <nav aria-label="Path" style={{ flex: 1, minWidth: 0, overflow: 'hidden' }}>
          <ol style={{ display: 'flex', alignItems: 'center', gap: 2, margin: 0, padding: 0, listStyle: 'none', whiteSpace: 'nowrap', overflowX: 'auto', scrollbarWidth: 'none',
            fontFamily: 'var(--cl-font-mono)', fontSize: D.mono, color: 'var(--cl-text-muted)' }}>
            {segs.map((s, i) => {
              const last = i === segs.length - 1;
              return (
                <li key={s.path} style={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                  {i > 0 && segs[i - 1].label !== '/' ? <span aria-hidden="true" style={{ color: 'var(--cl-border)' }}>/</span> : null}
                  {last
                    ? <span aria-current="location" style={{ padding: '2px 4px', color: 'var(--cl-primary-text)' }}>{s.label}</span>
                    : <button type="button" onClick={() => onNavigate && onNavigate(s.path)} style={crumbBtn}
                        onMouseEnter={(e) => { e.currentTarget.style.color = 'var(--cl-text)'; }} onMouseLeave={(e) => { e.currentTarget.style.color = ''; }}>{s.label}</button>}
                </li>
              );
            })}
          </ol>
        </nav>
        {toolbar ? <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>{toolbar}</div> : null}
      </div>
      <div style={{ height: 0, position: 'relative' }}>
        {loading ? <ProgressBar size="sm" aria-label={'Loading ' + path} style={{ position: 'absolute', left: 0, right: 0, top: -2 }} /> : null}
      </div>
      <div style={{ overflow: 'auto', maxHeight }}>
        {error ? (
          <div role="alert" style={{ padding: '28px 16px', textAlign: 'center', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 12 }}>
            <span style={{ color: 'var(--cl-danger-text)', fontSize: D.fs }}>{error}</span>
            {onRetry ? <Button variant="outline" size="sm" onClick={onRetry}>Try again</Button> : null}
          </div>
        ) : (
          <table aria-label={label} aria-busy={loading || undefined} aria-multiselectable="true" style={{ width: '100%', borderCollapse: 'separate', borderSpacing: 0, opacity: loading ? 0.6 : 1 }}>
            <thead><tr>{head('name', 'Name')}{cols.map((k) => head(k, COLS[k].label, COLS[k]))}</tr></thead>
            <tbody ref={body}>
              {rows.length === 0 ? (
                <tr><td colSpan={cols.length + 1} style={{ padding: '28px 16px', textAlign: 'center', color: 'var(--cl-text-muted)', fontSize: D.fs }}>{loading ? 'Loading…' : empty}</td></tr>
              ) : rows.map((e, i) => {
                const on = sel.has(e.name), hidden = e.name.startsWith('.');
                const bg = on ? 'var(--cl-primary-glow)' : 'transparent';
                const td = { padding: `0 ${D.px}px`, height: D.row, boxSizing: 'border-box', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                  borderBottom: '1px solid var(--cl-border)', fontFamily: 'var(--cl-font-mono)', fontSize: D.mono, fontVariantNumeric: 'tabular-nums' };
                return (
                  <tr key={e.name} data-row tabIndex={i === focus ? 0 : -1} aria-selected={on}
                    onClick={(ev) => (openOnClick ? activate(e) : pick(i, ev))} onDoubleClick={() => !openOnClick && activate(e)}
                    onKeyDown={(ev) => onKey(ev, i)} onFocus={() => { setFocus(i); setFocused(true); }} onBlur={() => setFocused(false)}
                    style={{ background: bg, cursor: 'default', outline: 'none', boxShadow: focused && i === focus ? 'inset 2px 0 0 var(--cl-primary)' : 'none' }}
                    onMouseEnter={(ev) => { if (!on) ev.currentTarget.style.background = 'var(--cl-surface-hover)'; }}
                    onMouseLeave={(ev) => { ev.currentTarget.style.background = bg; }}>
                    <td style={{ ...td, maxWidth: 0, width: '100%', color: hidden ? 'var(--cl-text-muted)' : 'var(--cl-text)' }} title={e.name + (e.target ? ' → ' + e.target : '')}>
                      <span style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                        <Glyph kind={e.kind} />
                        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{e.name}{e.kind === 'dir' ? '/' : ''}</span>
                        {e.target ? <span style={{ color: 'var(--cl-text-muted)', overflow: 'hidden', textOverflow: 'ellipsis' }}>{'→ ' + e.target}</span> : null}
                      </span>
                    </td>
                    {cols.map((k) => (
                      <td key={k} style={{ ...td, width: COLS[k].width, textAlign: COLS[k].numeric ? 'right' : 'left', color: 'var(--cl-text-muted)' }}>{COLS[k].value(e)}</td>
                    ))}
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
