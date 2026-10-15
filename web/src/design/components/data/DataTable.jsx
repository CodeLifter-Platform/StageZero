import React from 'react';

/** Row height, cell padding and type per density. Tight is the point of this component. */
export const TABLE_DENSITY = {
  tight:    { row: 22, px: 8,  fs: 12,   mono: 11.5, head: 22 },
  compact:  { row: 28, px: 10, fs: 13,   mono: 12,   head: 28 },
  standard: { row: 36, px: 14, fs: 14,   mono: 13,   head: 34 },
  relaxed:  { row: 48, px: 16, fs: 15,   mono: 14,   head: 40 },
};

/**
 * DataTable — sortable, selectable rows at four densities, from 22px tight to 48px relaxed.
 * Row height is fixed per density so a dense table never jitters; long values truncate
 * with a tooltip instead of wrapping. Numbers right-align in mono with tabular figures.
 */
export function DataTable({
  columns = [], rows = [], rowKey = (r, i) => r.id ?? i,
  density = 'compact', onDensityChange, sort, onSortChange,
  selectable = false, selected, onSelectedChange, onRowClick,
  striped = false, stickyHeader = true, maxHeight, empty = 'No rows', caption, style, ...rest
}) {
  const D = TABLE_DENSITY[density] || TABLE_DENSITY.compact;
  const [innerSort, setInnerSort] = React.useState(null);
  const [innerSel, setInnerSel] = React.useState(() => new Set());
  const s = sort !== undefined ? sort : innerSort;
  const sel = selected ? new Set(selected) : innerSel;
  const setSel = (n) => { if (!selected) setInnerSel(n); onSelectedChange && onSelectedChange([...n]); };

  const sorted = React.useMemo(() => {
    if (!s) return rows;
    const col = columns.find((c) => c.key === s.key); if (!col) return rows;
    const get = col.sortValue || ((r) => r[col.key]);
    return [...rows].sort((a, b) => { const x = get(a), y = get(b); const c = x < y ? -1 : x > y ? 1 : 0; return s.dir === 'desc' ? -c : c; });
  }, [rows, s, columns]);

  const toggleSort = (c) => {
    if (!c.sortable) return;
    const next = !s || s.key !== c.key ? { key: c.key, dir: 'asc' } : s.dir === 'asc' ? { key: c.key, dir: 'desc' } : null;
    if (sort === undefined) setInnerSort(next); onSortChange && onSortChange(next);
  };
  const keys = sorted.map(rowKey);
  const all = keys.length > 0 && keys.every((k) => sel.has(k));
  const some = !all && keys.some((k) => sel.has(k));
  const cb = { width: Math.max(12, D.row - 12), height: Math.max(12, D.row - 12), maxWidth: 16, maxHeight: 16, accentColor: 'var(--cl-primary)', margin: 0, cursor: 'pointer' };
  const cell = (c) => ({
    padding: `0 ${D.px}px`, height: D.row, boxSizing: 'border-box', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
    textAlign: c.align || (c.numeric ? 'right' : 'left'), maxWidth: c.width || 320, width: c.width,
    fontFamily: c.mono || c.numeric ? 'var(--cl-font-mono)' : 'var(--cl-font-sans)', fontSize: c.mono || c.numeric ? D.mono : D.fs,
    fontVariantNumeric: 'tabular-nums', color: c.muted ? 'var(--cl-text-muted)' : 'var(--cl-text)', borderBottom: '1px solid var(--cl-border)',
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8, minWidth: 0, ...style }} {...rest}>
      {onDensityChange ? (
        <div role="radiogroup" aria-label="Row density" style={{ alignSelf: 'flex-end', display: 'flex', gap: 3, padding: 3, background: 'var(--cl-bg)', border: '1px solid var(--cl-border)', borderRadius: 'var(--cl-radius-md)' }}>
          {Object.keys(TABLE_DENSITY).map((k) => (
            <button key={k} type="button" role="radio" aria-checked={k === density} onClick={() => onDensityChange(k)} title={TABLE_DENSITY[k].row + 'px rows'}
              style={{ padding: '3px 9px', border: 'none', borderRadius: 'var(--cl-radius-sm)', cursor: 'pointer', textTransform: 'capitalize',
                font: '600 0.72rem var(--cl-font-sans)', background: k === density ? 'var(--cl-primary)' : 'transparent', color: k === density ? 'var(--cl-on-primary)' : 'var(--cl-text-muted)' }}>{k}</button>
          ))}
        </div>
      ) : null}
      <div style={{ overflow: 'auto', maxHeight, border: '1px solid var(--cl-border)', borderRadius: 'var(--cl-radius-lg)', background: 'var(--cl-surface)' }}>
        <table style={{ width: '100%', borderCollapse: 'separate', borderSpacing: 0 }}>
          {caption ? <caption style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>{caption}</caption> : null}
          <thead>
            <tr>
              {selectable ? (
                <th style={{ width: D.row, padding: `0 ${D.px}px`, height: D.head, background: 'var(--cl-bg)', borderBottom: '1px solid var(--cl-border)', position: stickyHeader ? 'sticky' : 'static', top: 0, zIndex: 1 }}>
                  <input type="checkbox" aria-label="Select all rows" checked={all} ref={(el) => el && (el.indeterminate = some)}
                    onChange={() => { const n = new Set(sel); keys.forEach((k) => (all ? n.delete(k) : n.add(k))); setSel(n); }} style={cb} />
                </th>
              ) : null}
              {columns.map((c) => {
                const on = s && s.key === c.key;
                return (
                  <th key={c.key} scope="col" aria-sort={on ? (s.dir === 'asc' ? 'ascending' : 'descending') : c.sortable ? 'none' : undefined}
                    style={{ ...cell(c), height: D.head, fontFamily: 'var(--cl-font-mono)', fontSize: Math.min(11, D.mono), fontWeight: 500, letterSpacing: '0.08em',
                      textTransform: 'uppercase', color: on ? 'var(--cl-text)' : 'var(--cl-text-muted)', background: 'var(--cl-bg)',
                      position: stickyHeader ? 'sticky' : 'static', top: 0, zIndex: 1 }}>
                    {c.sortable ? (
                      <button type="button" onClick={() => toggleSort(c)} style={{ all: 'unset', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: 5, flexDirection: (c.align || (c.numeric ? 'right' : 'left')) === 'right' ? 'row-reverse' : 'row' }}>
                        {c.label}<span aria-hidden="true" style={{ fontSize: '0.8em', opacity: on ? 1 : 0.35 }}>{on && s.dir === 'desc' ? '\u25bc' : '\u25b2'}</span>
                      </button>
                    ) : c.label}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {sorted.length === 0 ? (
              <tr><td colSpan={columns.length + (selectable ? 1 : 0)} style={{ padding: '28px 16px', textAlign: 'center', color: 'var(--cl-text-muted)', fontSize: D.fs }}>{empty}</td></tr>
            ) : sorted.map((r, i) => {
              const k = keys[i], on = sel.has(k);
              const bg = on ? 'var(--cl-primary-glow)' : striped && i % 2 ? 'color-mix(in srgb, var(--cl-bg) 55%, transparent)' : 'transparent';
              return (
                <tr key={k} aria-selected={selectable ? on : undefined} onClick={onRowClick ? () => onRowClick(r) : undefined}
                  style={{ background: bg, cursor: onRowClick ? 'pointer' : 'default' }}
                  onMouseEnter={(e) => { if (!on) e.currentTarget.style.background = 'var(--cl-surface-hover)'; }}
                  onMouseLeave={(e) => { e.currentTarget.style.background = bg; }}>
                  {selectable ? (
                    <td style={{ padding: `0 ${D.px}px`, height: D.row, borderBottom: '1px solid var(--cl-border)' }} onClick={(e) => e.stopPropagation()}>
                      <input type="checkbox" aria-label={'Select row ' + (i + 1)} checked={on} onChange={() => { const n = new Set(sel); on ? n.delete(k) : n.add(k); setSel(n); }} style={cb} />
                    </td>
                  ) : null}
                  {columns.map((c) => {
                    const v = c.render ? c.render(r, density) : r[c.key];
                    return <td key={c.key} title={typeof v === 'string' || typeof v === 'number' ? String(v) : undefined} style={cell(c)}>{v}</td>;
                  })}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
