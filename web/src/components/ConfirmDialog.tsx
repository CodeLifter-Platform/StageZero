import { Button, Dialog } from '../ui';

/** Two-step delete: nothing is removed until the person confirms in words they can read. */
export function ConfirmDialog({ open, title, message, confirmLabel = 'Delete', busy = false, onConfirm, onCancel }: {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <Dialog
      open={open}
      onClose={busy ? undefined : onCancel}
      title={title}
      footer={<>
        <Button variant="ghost" onClick={onCancel} disabled={busy}>Cancel</Button>
        <Button onClick={onConfirm} disabled={busy}>{busy ? 'Working…' : confirmLabel}</Button>
      </>}
    >
      {message}
    </Dialog>
  );
}
