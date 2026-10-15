import React from 'react';

/** Controlled toggle switch. Track fills purple when on. */
export interface SwitchProps {
  /** Current on/off state (controlled). */
  checked?: boolean;
  /** Called with the next boolean value on toggle. */
  onChange?: (next: boolean) => void;
  disabled?: boolean;
  style?: React.CSSProperties;
}

export function Switch(props: SwitchProps): JSX.Element;
