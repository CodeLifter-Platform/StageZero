import React from 'react';

/**
 * Sidebar — app navigation. Grouped sections, one level of nesting, counts,
 * a filter box and a footer slot. Collapses to a 60px icon rail; in the rail,
 * labels become tooltips and nested items are reached through their parent.
 */
export function Sidebar({
  sections = [],
  value,
  onSelect,
  collapsed = false,
  onToggleCollapsed,
  header,
  footer,
  searchable = false,
  width = 240,
  style,
  ...rest
}) {
  const [q, setQ] = React.useState('');
  const [open, setOpen] = React.useState(() => new Set(sections.flatMap((s) => s.items.filter((i) => i.children && i.children.some((c) => c.id === value)).map((i) => i.id))));
  const needle = q.trim().toLowerCase();
  const match = (i) => !needle || i.label.toLowerCase().includes(needle);

  const filtered = sections.map((s) => ({
    ...s,
    items: s.items
      .map((i) => (i.children ? { ...i, children: i.children.filter(match) } : i))
      .filter((i) => match(i) || (i.children && i.children.length)),
  })).filter((s) => s.items.length);

  const row = (i, depth) => {
    const on = i.id === value;
    const hasKids = !!(i.children && i.children.length);
    const expanded = needle ? true : open.has(i.id);
    const childActive = hasKids && i.children.some((c) => c.id === value);
    return (
      <React.Fragment key={i.id}>
        <button
          type="button"
          title={collapsed ? i.label : undefined}
          aria-current={on ? 'page' : undefined}
          aria-expanded={hasKids && !collapsed ? expanded : undefined}
          onClick={() => {
            if (hasKids && !collapsed) setOpen((p) => { const n = new Set(p); n.has(i.id) ? n.delete(i.id) : n.add(i.id); return n; });
            else onSelect && onSelect(i.id);
          }}
          style={{
            display: 'flex', alignItems: 'center', gap: 10, width: '100%', textAlign: 'left',
            padding: collapsed ? '9px 0' : `var(--cl-row-py) 10px var(--cl-row-py) ${depth ? 34 : 10}px`,
            justifyContent: collapsed ? 'center' : 'flex-start',
            border: 'none', borderRadius: 'var(--cl-radius-sm)', cursor: 'pointer',
            fontFamily: 'var(--cl-font-sans)', fontSize: depth ? 'var(--cl-text-sm)' : 'var(--cl-ctrl-fs)',
            fontWeight: on ? 600 : 500, whiteSpace: 'nowrap',
            background: on ? 'var(--cl-primary-glow)' : 'transparent',
            color: on ? 'var(--cl-primary-text)' : childActive ? 'var(--cl-text)' : 'var(--cl-text-muted)',
          }}
        >
          {i.icon && depth === 0 ? <span aria-hidden="true" style={{ width: 18, textAlign: 'center', flexShrink: 0 }}>{i.icon}</span> : null}
          {collapsed ? null : <span style={{ flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' }}>{i.label}</span>}
          {!collapsed && i.count != null ? (
            <span style={{
              fontFamily: 'var(--cl-font-mono)', fontSize: '0.68rem', padding: '1px 7px', borderRadius: 'var(--cl-radius-full)',
              background: i.countTone === 'attention' ? 'var(--cl-primary)' : 'var(--cl-surface)',
              color: i.countTone === 'attention' ? 'var(--cl-on-primary)' : 'var(--cl-text-muted)',
              border: i.countTone === 'attention' ? 'none' : '1px solid var(--cl-border)',
            }}>{i.count}</span>
          ) : null}
          {!collapsed && hasKids ? (
            <span aria-hidden="true" style={{ fontSize: '0.6rem', transition: 'transform var(--cl-dur-fast) var(--cl-ease)', transform: expanded ? 'rotate(90deg)' : 'none' }}>{'\u25b6'}</span>
          ) : null}
        </button>
        {hasKids && expanded && !collapsed ? i.children.map((c) => row(c, 1)) : null}
      </React.Fragment>
    );
  };

  return (
    <nav aria-label="Main" style={{
      width: collapsed ? 60 : width, flexShrink: 0, height: '100%', display: 'flex', flexDirection: 'column',
      background: 'var(--cl-bg)', borderRight: '1px solid var(--cl-border)',
      transition: 'width var(--cl-dur) var(--cl-ease)', overflow: 'hidden', ...style,
    }} {...rest}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, padding: collapsed ? '14px 0' : '14px 14px 10px', justifyContent: collapsed ? 'center' : 'space-between' }}>
        {collapsed ? null : header}
        {onToggleCollapsed ? (
          <button type="button" onClick={onToggleCollapsed} aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            style={{ border: '1px solid var(--cl-border)', background: 'transparent', color: 'var(--cl-text-muted)', borderRadius: 'var(--cl-radius-sm)', width: 28, height: 28, cursor: 'pointer', fontFamily: 'var(--cl-font-mono)' }}>
            {collapsed ? '\u00bb' : '\u00ab'}
          </button>
        ) : null}
      </div>
      {searchable && !collapsed ? (
        <div style={{ padding: '0 12px 8px' }}>
          <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Filter\u2026" aria-label="Filter navigation"
            style={{ width: '100%', boxSizing: 'border-box', padding: '6px 10px', fontSize: 'var(--cl-text-sm)', fontFamily: 'var(--cl-font-sans)',
              background: 'var(--cl-surface)', border: '1px solid var(--cl-border)', borderRadius: 'var(--cl-radius-sm)', color: 'var(--cl-text)', outline: 'none' }} />
        </div>
      ) : null}
      <div style={{ flex: 1, overflowY: 'auto', padding: collapsed ? '0 10px' : '0 8px', display: 'flex', flexDirection: 'column', gap: 1 }}>
        {filtered.map((s, si) => (
          <div key={s.title || si} style={{ display: 'flex', flexDirection: 'column', gap: 1, marginTop: si ? 12 : 0 }}>
            {s.title && !collapsed ? (
              <div style={{ padding: '6px 10px 4px', fontFamily: 'var(--cl-font-mono)', fontSize: '0.62rem', letterSpacing: '0.14em', textTransform: 'uppercase', color: 'var(--cl-text-muted)' }}>{s.title}</div>
            ) : s.title && si ? <div style={{ height: 1, background: 'var(--cl-border)', margin: '4px 6px 8px' }} /> : null}
            {s.items.map((i) => row(i, 0))}
          </div>
        ))}
        {!filtered.length ? <div style={{ padding: '10px', fontSize: 'var(--cl-text-sm)', color: 'var(--cl-text-muted)' }}>No matches</div> : null}
      </div>
      {footer ? <div style={{ borderTop: '1px solid var(--cl-border)', padding: collapsed ? '10px 0' : '10px 12px', display: 'flex', justifyContent: collapsed ? 'center' : 'flex-start' }}>{footer}</div> : null}
    </nav>
  );
}
