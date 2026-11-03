import React from 'react';
import { TableDensity } from './DataTable';

export interface FileEntry {
  /** File name only, no path. Names starting with "." are hidden unless `showHidden`. */
  name: string;
  /** `dir` opens as a folder; `link` shows its target after the name. */
  kind: 'dir' | 'file' | 'link' | 'other';
  /** Bytes. Shown binary-stepped, like `ls -lh`. Ignored for folders. */
  size?: number;
  /** Date, ISO string or epoch ms. Shown as "2026-10-20 14:03" in local time. */
  modified?: Date | string | number;
  /** Permission string as `ls -l` prints it, e.g. "-rw-r--r--". */
  mode?: string;
  /** Where a link points. */
  target?: string;
}

/**
 * FileBrowser — one folder of a remote file system: path bar, folders first, size and date.
 * Controlled by `path`; you fetch `entries` for it and pass them in.
 *
 * @startingPoint section="Data" subtitle="Remote folder listing with a path bar" viewport="760x420"
 */
export interface FileBrowserProps extends React.HTMLAttributes<HTMLDivElement> {
  /** The folder shown: absolute ("/var/log") or home-relative ("~/src"). */
  path: string;
  /** The folder's contents, in any order. */
  entries: FileEntry[];
  /** Called with a new path: a breadcrumb, the up button, Backspace, or opening a folder. */
  onNavigate?: (path: string) => void;
  /** Called when a file or link is opened (double-click or Enter). */
  onOpen?: (entry: FileEntry) => void;
  /** Controlled selection, by name. Cleared when `path` changes. */
  selected?: string[];
  onSelectedChange?: (names: string[]) => void;
  /** Columns after Name, in order. @default ["size", "modified"] */
  columns?: Array<'size' | 'modified' | 'mode'>;
  /** Row metrics, shared with DataTable. @default "compact" */
  density?: TableDensity;
  /** Show dotfiles, in muted text. @default false */
  showHidden?: boolean;
  /** A thin indeterminate bar under the path; current rows dim but stay. @default false */
  loading?: boolean;
  /** Replaces the list, in danger text: "Permission denied", "No such folder". */
  error?: React.ReactNode;
  /** Adds a "Try again" button under `error`. */
  onRetry?: () => void;
  /** Controls at the right of the path bar: refresh, upload, show hidden. */
  toolbar?: React.ReactNode;
  /** Shown for a folder with nothing visible in it. @default "This folder is empty" */
  empty?: React.ReactNode;
  /** One tap opens instead of selecting. Use on touch screens. @default false */
  openOnClick?: boolean;
  /** Scrolls inside the list past this height (px or CSS). */
  maxHeight?: number | string;
  /** Accessible name of the list. @default "Files" */
  label?: string;
}

export function FileBrowser(props: FileBrowserProps): JSX.Element;
/** Folders first in either direction; names in natural order. Hidden entries are not filtered here. */
export function sortEntries(entries: FileEntry[], key?: 'name' | 'size' | 'modified' | 'mode', dir?: 'asc' | 'desc'): FileEntry[];
export function formatSize(bytes?: number): string;
export function formatModified(value?: Date | string | number): string;
export function parentPath(path: string): string | null;
export function joinPath(path: string, name: string): string;
export function pathSegments(path: string): Array<{ label: string; path: string }>;
