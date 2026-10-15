import { useEffect, useState } from 'react';
import { api, subscribeToIp } from '../api/client';
import { Tag } from '../ui';

/**
 * The public IP in the top bar. Rendered only inside the signed-in shell: a Cloudflare
 * Tunnel exists to hide this address, so an anonymous visitor never loads it.
 */
export function IpChip() {
  const [ip, setIp] = useState<string | null | undefined>(undefined);

  useEffect(() => {
    let live = true;
    api.ip.current().then((c) => { if (live) setIp(c.ipAddress ?? ''); }).catch(() => { if (live) setIp(null); });
    // Pushed by whichever check sees the change; no polling.
    const close = subscribeToIp((c) => { if (live) setIp(c.ipAddress ?? ''); });
    return () => { live = false; close(); };
  }, []);

  // undefined: loading; '': no check has found an IP yet; null: couldn't ask.
  const label = ip === undefined ? 'Loading…' : ip === null ? 'Unknown' : ip || 'No IP yet';
  return <Tag title="Current public IP" aria-label={`Current public IP: ${label}`}>{label}</Tag>;
}
