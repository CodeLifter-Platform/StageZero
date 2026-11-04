import React, { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

/** Three vertical dots, drawn in currentColor so the trigger's text color sets it. */
export function EllipsisVerticalIcon({ size = 16 }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false">
      <circle cx="8" cy="3" r="1.6" />
      <circle cx="8" cy="8" r="1.6" />
      <circle cx="8" cy="13" r="1.6" />
    </svg>
  );
}

const GAP = 4;
const EDGE = 8;

function DefaultTrigger({ label, disabled, ...rest }) {
  const [hover, setHover] = useState(false);
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      disabled={disabled}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
        width: 28, height: 28, padding: 0,
        background: hover && !disabled ? 'var(--cl-surface-hover)' : 'transparent',
        color: hover && !disabled ? 'var(--cl-text)' : 'var(--cl-text-muted)',
        border: '1px solid transparent',
        borderRadius: 'var(--cl-radius-md)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.4 : 1,
        transition: 'background var(--cl-dur-fast) var(--cl-ease), color var(--cl-dur-fast) var(--cl-ease)',
      }}
      {...rest}
    >
      <EllipsisVerticalIcon />
    </button>
  );
}

const isActionable = (item) => item && !item.separator && !item.disabled;

/**
 * Menu — a trigger that opens a floating list of actions. The default trigger is a
 * vertical-ellipsis icon button (row actions in tables); pass `trigger` to open it from
 * any element instead: a labelled Button, an Avatar, a Tag. The panel renders in a portal,
 * so a DataTable's overflow never clips it, and flips above the trigger near the bottom
 * of the viewport.
 */
export function Menu({
  items,
  trigger,
  label = 'More actions',
  align = 'end',
  disabled = false,
  minWidth = 180,
  style,
}) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const [pos, setPos] = useState(null);
  const anchorRef = useRef(null);
  const panelRef = useRef(null);
  const itemRefs = useRef([]);
  const menuId = useId();

  const focusTrigger = () => {
    const el = anchorRef.current?.querySelector('button, [href], [tabindex]:not([tabindex="-1"])');
    el?.focus();
  };

  const first = () => items.findIndex(isActionable);
  const last = () => {
    for (let i = items.length - 1; i >= 0; i--) if (isActionable(items[i])) return i;
    return -1;
  };
  const step = (from, dir) => {
    if (!items.some(isActionable)) return -1;
    let i = from;
    do { i = (i + dir + items.length) % items.length; } while (!isActionable(items[i]));
    return i;
  };

  const openAt = (index) => {
    if (disabled) return;
    setActive(index);
    setOpen(true);
  };
  const close = useCallback((refocus) => {
    setOpen(false);
    setActive(-1);
    setPos(null);
    if (refocus) focusTrigger();
  }, []);

  const place = useCallback(() => {
    const anchor = anchorRef.current;
    const panel = panelRef.current;
    if (!anchor || !panel) return;
    const a = anchor.getBoundingClientRect();
    const w = panel.offsetWidth;
    const h = panel.offsetHeight;
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    let top = a.bottom + GAP;
    if (top + h > vh - EDGE && a.top - GAP - h >= EDGE) top = a.top - GAP - h;
    let left = align === 'start' ? a.left : a.right - w;
    left = Math.min(Math.max(left, EDGE), Math.max(EDGE, vw - EDGE - w));
    setPos({ top, left });
  }, [align]);

  useLayoutEffect(() => {
    if (open) place();
  }, [open, place, items.length]);

  useEffect(() => {
    if (!open) return undefined;
    const onPointer = (e) => {
      if (panelRef.current?.contains(e.target) || anchorRef.current?.contains(e.target)) return;
      close(false);
    };
    window.addEventListener('pointerdown', onPointer, true);
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true);
    return () => {
      window.removeEventListener('pointerdown', onPointer, true);
      window.removeEventListener('resize', place);
      window.removeEventListener('scroll', place, true);
    };
  }, [open, place, close]);

  useEffect(() => {
    if (open && active >= 0) itemRefs.current[active]?.focus();
    else if (open) panelRef.current?.focus();
  }, [open, active, pos]);

  const select = (item) => {
    close(true);
    item.onSelect?.();
  };

  const onTriggerKeyDown = (e) => {
    if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      openAt(first());
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      openAt(last());
    }
  };

  const onPanelKeyDown = (e) => {
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); setActive((i) => step(i < 0 ? -1 : i, 1)); break;
      case 'ArrowUp': e.preventDefault(); setActive((i) => step(i < 0 ? 0 : i, -1)); break;
      case 'Home': e.preventDefault(); setActive(first()); break;
      case 'End': e.preventDefault(); setActive(last()); break;
      case 'Escape': e.preventDefault(); close(true); break;
      case 'Tab': close(false); break;
      default: break;
    }
  };

  const triggerProps = {
    'aria-haspopup': 'menu',
    'aria-expanded': open,
    'aria-controls': open ? menuId : undefined,
    disabled,
    onClick: (e) => {
      trigger?.props?.onClick?.(e);
      if (open) close(false);
      // A pointer click opens with nothing highlighted; Enter/Space arrive as keydown first.
      else openAt(e.detail === 0 ? first() : -1);
    },
    onKeyDown: (e) => {
      trigger?.props?.onKeyDown?.(e);
      if (!open) onTriggerKeyDown(e);
    },
  };

  const triggerNode = React.isValidElement(trigger)
    ? React.cloneElement(trigger, triggerProps)
    : <DefaultTrigger label={label} {...triggerProps} />;

  itemRefs.current = [];

  return (
    <span ref={anchorRef} style={{ display: 'inline-flex', ...style }}>
      {triggerNode}
      {open && typeof document !== 'undefined' ? createPortal(
        <div
          ref={panelRef}
          id={menuId}
          role="menu"
          aria-label={label}
          tabIndex={-1}
          onKeyDown={onPanelKeyDown}
          style={{
            position: 'fixed',
            top: pos ? pos.top : -9999,
            left: pos ? pos.left : -9999,
            visibility: pos ? 'visible' : 'hidden',
            zIndex: 110,
            minWidth,
            maxWidth: 320,
            padding: 4,
            boxSizing: 'border-box',
            background: 'var(--cl-surface)',
            border: '1px solid var(--cl-border)',
            borderRadius: 'var(--cl-radius-md)',
            boxShadow: 'var(--cl-shadow-md)',
            fontFamily: 'var(--cl-font-sans)',
            outline: 'none',
          }}
        >
          {items.map((item, i) => {
            if (item.separator) {
              return <div key={`sep-${i}`} role="separator" style={{ height: 1, margin: '4px 0', background: 'var(--cl-border)' }} />;
            }
            const isActive = i === active;
            const danger = item.tone === 'danger';
            return (
              <button
                key={item.key ?? `${item.label}-${i}`}
                ref={(el) => { itemRefs.current[i] = el; }}
                type="button"
                role="menuitem"
                tabIndex={isActive ? 0 : -1}
                aria-disabled={item.disabled || undefined}
                onClick={() => { if (!item.disabled) select(item); }}
                onMouseEnter={() => setActive(isActionable(item) ? i : -1)}
                onMouseLeave={() => setActive(-1)}
                style={{
                  display: 'flex', alignItems: 'flex-start', gap: 10, width: '100%',
                  padding: '7px 10px', boxSizing: 'border-box',
                  background: isActive ? 'var(--cl-surface-hover)' : 'transparent',
                  border: 'none', borderRadius: 'var(--cl-radius-sm)',
                  color: danger ? 'var(--cl-danger-text)' : 'var(--cl-text)',
                  fontFamily: 'inherit', fontSize: 'var(--cl-text-sm)', fontWeight: 'var(--cl-weight-medium)',
                  textAlign: 'left', whiteSpace: 'nowrap',
                  cursor: item.disabled ? 'not-allowed' : 'pointer',
                  opacity: item.disabled ? 0.4 : 1,
                  outline: 'none',
                }}
              >
                {item.icon ? <span aria-hidden="true" style={{ display: 'inline-flex', marginTop: 2 }}>{item.icon}</span> : null}
                <span style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
                  <span>{item.label}</span>
                  {item.hint ? (
                    <span style={{ fontSize: 'var(--cl-text-xs)', fontWeight: 'var(--cl-weight-regular)', color: 'var(--cl-text-muted)', whiteSpace: 'normal' }}>{item.hint}</span>
                  ) : null}
                </span>
              </button>
            );
          })}
        </div>,
        document.body,
      ) : null}
    </span>
  );
}
