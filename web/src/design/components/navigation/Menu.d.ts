import React from 'react';

/** One action in a Menu. */
export interface MenuActionItem {
  /** Stable key; defaults to the label. */
  key?: string;
  /** Visible label, and the item's accessible name. */
  label: string;
  /** Runs after the menu closes and focus returns to the trigger. */
  onSelect?: () => void;
  /** Shown at 40% and skipped by arrow keys; it can't be chosen. */
  disabled?: boolean;
  /** `danger` colors the label for destructive actions (delete, revoke). @default "default" */
  tone?: 'default' | 'danger';
  /** Short muted line under the label, e.g. why an item is disabled. */
  hint?: string;
  /** Optional leading glyph. */
  icon?: React.ReactNode;
  separator?: false;
}

/** A hairline between groups, e.g. to set destructive actions apart. */
export interface MenuSeparator {
  separator: true;
}

export type MenuItem = MenuActionItem | MenuSeparator;

/**
 * A trigger that opens a floating list of actions. With no `trigger` it renders the
 * vertical-ellipsis icon button used for row actions in tables; pass any element as
 * `trigger` to open it from a labelled Button, an Avatar, a Tag, or anything else that
 * spreads `onClick`, `onKeyDown`, `disabled` and `aria-*` props onto a focusable element.
 *
 * @startingPoint section="Navigation" subtitle="Row actions behind a trigger" viewport="700x320"
 */
export interface MenuProps {
  /** Actions in display order, with optional separators. */
  items: MenuItem[];
  /** Element that opens the menu. Omit for the vertical-ellipsis icon button. */
  trigger?: React.ReactElement;
  /** Accessible name of the menu, and of the default trigger (`aria-label` + tooltip). Name the row: "Actions for api.example.com". @default "More actions" */
  label?: string;
  /** Which trigger edge the panel lines up with. @default "end" */
  align?: 'start' | 'end';
  /** Disables the trigger. */
  disabled?: boolean;
  /** Minimum panel width in px. @default 180 */
  minWidth?: number;
  /** Style for the inline wrapper around the trigger. */
  style?: React.CSSProperties;
}

export function Menu(props: MenuProps): JSX.Element;

/** The three-dot glyph the default trigger draws, in currentColor. */
export function EllipsisVerticalIcon(props: { size?: number }): JSX.Element;
