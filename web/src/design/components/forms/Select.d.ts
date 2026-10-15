import React from 'react';

/** Native select styled to match CodeLifter inputs. */
export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  children?: React.ReactNode;
}

export function Select(props: SelectProps): JSX.Element;
