import React from 'react';

export type TableDensity = 'tight' | 'compact' | 'standard' | 'relaxed';

export interface DataColumn<R = any> {
  key: string;
  label: string;
  /** Right-aligned, mono, tabular figures. */
  numeric?: boolean;
  /** Mono without right alignment: ids, paths, hashes, image tags. */
  mono?: boolean;
  /** Secondary column in muted text. */
  muted?: boolean;
  align?: 'left' | 'right' | 'center';
  /** px. Values past the width truncate with a tooltip. */
  width?: number;
  sortable?: boolean;
  /** What to sort by when the displayed value isn't sortable ("128 MB" → 128). */
  sortValue?: (row: R) => number | string;
  /** Custom cell. Receives the density so a cell can drop decoration when tight. */
  render?: (row: R, density: TableDensity) => React.ReactNode;
}

/**
 * DataTable — sortable, selectable rows at four densities: tight 22px, compact 28px,
 * standard 36px, relaxed 48px.
 *
 * @startingPoint section="Data" subtitle="Dense sortable data table" viewport="760x420"
 */
export interface DataTableProps<R = any> extends React.HTMLAttributes<HTMLDivElement> {
  columns: DataColumn<R>[];
  rows: R[];
  /** @default (row, i) => row.id ?? i */
  rowKey?: (row: R, index: number) => string | number;
  /** @default "compact" */
  density?: TableDensity;
  /** Shows the density switcher above the table. Persist the choice per user. */
  onDensityChange?: (d: TableDensity) => void;
  /** Controlled sort. null = original order. */
  sort?: { key: string; dir: 'asc' | 'desc' } | null;
  onSortChange?: (s: { key: string; dir: 'asc' | 'desc' } | null) => void;
  /** Adds a checkbox column with select-all. @default false */
  selectable?: boolean;
  /** Controlled selection (row keys). */
  selected?: Array<string | number>;
  onSelectedChange?: (keys: Array<string | number>) => void;
  onRowClick?: (row: R) => void;
  /** Alternating row tint. Helps the eye track across wide tight tables. @default false */
  striped?: boolean;
  /** @default true */
  stickyHeader?: boolean;
  /** Scrolls inside the table past this height (px or CSS). */
  maxHeight?: number | string;
  /** Shown when there are no rows. @default "No rows" */
  empty?: React.ReactNode;
  /** Accessible table name. */
  caption?: string;
}

export function DataTable<R = any>(props: DataTableProps<R>): JSX.Element;
export const TABLE_DENSITY: Record<TableDensity, { row: number; px: number; fs: number; mono: number; head: number }>;
