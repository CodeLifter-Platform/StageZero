import React from 'react';

export interface TabItem {
  id: string;
  label: string;
}

/** Underline tab bar. Controlled via `value` / `onChange`. */
export interface TabsProps {
  /** Tab definitions in display order. */
  tabs: TabItem[];
  /** Currently-active tab id. */
  value: string;
  /** Called with the next tab id on click. */
  onChange?: (id: string) => void;
  style?: React.CSSProperties;
}

export function Tabs(props: TabsProps): JSX.Element;
