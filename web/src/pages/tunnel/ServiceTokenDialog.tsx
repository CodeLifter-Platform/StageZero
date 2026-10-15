import { useState } from 'react';
import type { NewServiceToken } from '../../api/types';
import { formatDateTime } from '../../app/format';
import { useToast } from '../../app/toast';
import { Notice } from '../../components/Notice';
import { Button, Checkbox, Dialog, Input } from '../../ui';

/**
 * A freshly minted Access service token. Cloudflare returns the client secret only at
 * creation and StageZero stores it nowhere, so this is the one chance to copy it: the
 * dialog can't be dismissed by the backdrop, and closes only once the person says it's saved.
 */
export function ServiceTokenDialog({ token, hostname, onDone }: {
  token: NewServiceToken | null;
  hostname: string;
  onDone: () => void;
}) {
  const notify = useToast();
  const [visible, setVisible] = useState(false);
  const [saved, setSaved] = useState(false);

  const copy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      notify('Copied to clipboard', 'success');
    } catch {
      // Clipboard access needs a secure context and permission. Falling back to "select it
      // yourself" beats losing the only copy of the secret.
      notify('Could not copy automatically. Select the value and copy it manually.', 'warning');
    }
  };

  const done = () => { setVisible(false); setSaved(false); onDone(); };

  return (
    <Dialog open={token !== null} title="Service token created" style={{ maxWidth: 620 }}
      footer={<>
        <Checkbox label="I have saved the client secret" checked={saved} onChange={(e) => setSaved(e.target.checked)} />
        <Button onClick={done} disabled={!saved}>Done</Button>
      </>}>
      {token ? (
        <div className="sz-stack">
          <Notice tone="warning" title="This will not be shown again.">
            Cloudflare returns the client secret only when the token is created, and StageZero doesn't store it.
            If you lose it you'll have to rotate the token and update every client.
          </Notice>
          <p className="sz-small">
            Service token <strong>{token.name}</strong> now guards <strong>{hostname}</strong>. Clients send these two values
            as the <code>CF-Access-Client-Id</code> and <code>CF-Access-Client-Secret</code> headers.
          </p>
          <div className="sz-row" style={{ alignItems: 'flex-end', flexWrap: 'nowrap' }}>
            <div style={{ flex: 1 }}><Input label="CF-Access-Client-Id" readOnly value={token.clientId} style={{ fontFamily: 'var(--cl-font-mono)' }} /></div>
            <Button variant="ghost" size="sm" onClick={() => void copy(token.clientId)}>Copy</Button>
          </div>
          <div className="sz-row" style={{ alignItems: 'flex-end', flexWrap: 'nowrap' }}>
            <div style={{ flex: 1 }}>
              <Input label="CF-Access-Client-Secret" readOnly type={visible ? 'text' : 'password'} value={token.clientSecret} style={{ fontFamily: 'var(--cl-font-mono)' }} />
            </div>
            <Button variant="ghost" size="sm" onClick={() => setVisible((v) => !v)}>{visible ? 'Hide' : 'Show'}</Button>
            <Button variant="outline" size="sm" onClick={() => void copy(token.clientSecret)}>Copy secret</Button>
          </div>
          {token.expiresAt ? <span className="sz-xs sz-muted">Expires {formatDateTime(token.expiresAt)}.</span> : null}
        </div>
      ) : null}
    </Dialog>
  );
}
