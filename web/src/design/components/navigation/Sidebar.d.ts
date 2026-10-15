import React from 'react';

export interface SidebarItem {
  id: string;
  label: string;
  /** Glyph or icon node. Top-level items only; required for a useful collapsed rail. */
  icon?: React.ReactNode;
  /** Count badge. Hidden in the rail. */
  count?: number | string;
  /** "attention" fills the badge in primary, for things waiting on the user. @default "neutral" */
  countTone?: 'neutral' | 'attention';
  /** One level only. A child cannot have children. */
  children?: SidebarItem[];
}

export interface SidebarSection {
  /** Mono uppercase header. Omit for the first, untitled group. */
  title?: string;
  items: SidebarItem[];
}

/**
 * Sidebar — app navigation with sections, one level of nesting, counts, filter,
 * footer, and a collapsible icon rail.
 *
 * @startingPoint section="Navigation" subtitle="App sidebar with icon rail" viewport="280x560"
 */
export interface SidebarProps extends React.HTMLAttributes<HTMLElement> {
  sections: SidebarSection[];
  /** id of the current page. */
  value?: string;
  onSelect?: (id: string) => void;
  /** Collapse to a 60px icon rail. @default false */
  collapsed?: boolean;
  /** Shows the collapse toggle. Omit to make the sidebar fixed. */
  onToggleCollapsed?: () => void;
  /** Logo / app name. Hidden in the rail. */
  header?: React.ReactNode;
  /** Account, status or a sync indicator. Keep it to one row. */
  footer?: React.ReactNode;
  /** Adds a filter box above the items. @default false */
  searchable?: boolean;
  /** Expanded width in px. @default 240 */
  width?: number;
}

export function Sidebar(props: SidebarProps): JSX.Element;
