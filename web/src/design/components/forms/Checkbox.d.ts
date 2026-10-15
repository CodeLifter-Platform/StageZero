import React from 'react';

/** Checkbox + label row. Uses native accent-color for the brand purple check. */
export interface CheckboxProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
}

export function Checkbox(props: CheckboxProps): JSX.Element;
