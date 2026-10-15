import React from 'react';

/** Monospace metadata tag (#docker, #prompts…). */
export interface TagProps extends React.HTMLAttributes<HTMLSpanElement> {
  children?: React.ReactNode;
}

export function Tag(props: TagProps): JSX.Element;
