import { ProgressBar } from '../ui';

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div style={{ maxWidth: 480, margin: 'var(--cl-space-3xl) auto' }}>
      <ProgressBar label={label} size="sm" />
    </div>
  );
}
