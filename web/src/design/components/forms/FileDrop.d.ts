import React from 'react';

export type RejectReason = 'type' | 'size' | 'count';

export interface RejectedFile {
  file: File;
  /** `type`: not in `accept`; `size`: over `maxSize`; `count`: a second file when `multiple` is false. */
  reason: RejectReason;
}

/**
 * FileDrop — a dashed zone that takes files by drag and drop, or opens the system picker on
 * click, Enter or Space. It hands the accepted files to `onFiles` and never uploads anything
 * itself.
 *
 * @startingPoint section="Forms" subtitle="Drag-and-drop file zone" viewport="560x200"
 */
export interface FileDropProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'onDrop'> {
  /** The files that passed `accept`, `maxSize` and `multiple`, in the order they arrived. */
  onFiles: (files: File[]) => void;
  /** The files that did not, each with why. Tell the person; the zone itself stays quiet. */
  onReject?: (rejected: RejectedFile[]) => void;
  /** As the file input's `accept`: ".png,.jpg", "image/*", "application/pdf". Unset accepts anything. */
  accept?: string;
  /** Allow several files per drop or pick. @default true */
  multiple?: boolean;
  /** Largest file in bytes. Larger ones are reported through `onReject`. */
  maxSize?: number;
  /** Greyed out and inert. @default false */
  disabled?: boolean;
  /** A transfer is running: the zone reads "Uploading…" and refuses more files until it ends. @default false */
  busy?: boolean;
  /** The prompt in the zone. @default "Drop files here, or browse" */
  label?: React.ReactNode;
  /** Under the zone, muted: the accepted types and the size limit, in words. */
  hint?: React.ReactNode;
  /** Replaces `hint`, in danger text, and paints the border danger. */
  error?: React.ReactNode;
  /** Zone height: `sm` for a toolbar or a dialog, `md` for a page. @default "md" */
  size?: 'sm' | 'md';
  /** Forwarded to the hidden file input, for a plain form post. */
  name?: string;
  /** Extra content under the prompt, inside the zone: a Button, a short list of what is queued. */
  children?: React.ReactNode;
}

export function FileDrop(props: FileDropProps): JSX.Element;
/** True when the file matches an `accept` string the way the browser's picker reads it. */
export function acceptsFile(file: File, accept?: string): boolean;
/** Splits files into accepted and rejected (with reasons) under the same rules the zone applies. */
export function filterFiles(files: ArrayLike<File>, rules?: { accept?: string; maxSize?: number; multiple?: boolean }): { accepted: File[]; rejected: RejectedFile[] };
/** One sentence per rejection, ready for a hint or a toast. */
export function describeRejections(rejected: RejectedFile[], rules?: { accept?: string; maxSize?: number }): string[];
/** "4.2 MB", binary-stepped, the same rule FileBrowser uses. */
export function formatBytes(bytes?: number): string;
