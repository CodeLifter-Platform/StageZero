import { useCallback, useEffect, useState } from 'react';
import { api, subscribeToIp } from '../api/client';
import type { CurrentIp, IpGroup } from '../api/types';
import { formatDateTime, relativeTime } from '../app/format';
import { useToast } from '../app/toast';
import { Loading } from '../components/Loading';
import { Notice } from '../components/Notice';
import { PageHeader } from '../components/PageHeader';
import { useDocumentTitle } from '../components/useDocumentTitle';
import { Badge, Button, Card, DataTable, type DataColumn } from '../ui';

const COLUMNS: DataColumn<IpGroup>[] = [
  { key: 'ipAddress', label: 'IP address', mono: true, sortable: true },
  {
    key: 'lastCheckedAt', label: 'Last checked', sortable: true, sortValue: (r) => r.lastCheckedAt,
    render: (r) => <span title={formatDateTime(r.lastCheckedAt)}>{relativeTime(r.lastCheckedAt)}</span>,
  },
  { key: 'firstSeenAt', label: 'First seen', muted: true, sortable: true, sortValue: (r) => r.firstSeenAt, render: (r) => formatDateTime(r.firstSeenAt) },
  { key: 'checkCount', label: 'Checks', numeric: true, sortable: true, render: (r) => <Badge tone="muted">{r.checkCount}</Badge> },
];

export function IpMonitorPage() {
  useDocumentTitle('IP Monitor');
  const notify = useToast();
  const [current, setCurrent] = useState<CurrentIp | null>(null);
  const [history, setHistory] = useState<IpGroup[] | null>(null);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [ip, groups] = await Promise.all([api.ip.current(), api.ip.history()]);
      setCurrent(ip);
      setHistory(groups);
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }, []);

  useEffect(() => {
    void load();
    // A change found by any check (the background monitor's included) refreshes the page.
    let first = true;
    return subscribeToIp((ip) => {
      setCurrent(ip);
      if (!first) void api.ip.history().then(setHistory).catch(() => {});
      first = false;
    });
  }, [load]);

  const checkNow = async () => {
    setChecking(true);
    try {
      const ip = await api.ip.check();
      setCurrent(ip);
      setHistory(await api.ip.history());
    } catch (err) {
      notify((err as Error).message, 'error');
    } finally {
      setChecking(false);
    }
  };

  if (!history && !error) return <Loading />;

  return (
    <div className="sz-page">
      <PageHeader title="IP Address Monitor" />
      {error ? <Notice tone="danger" title="Couldn't load the IP history">{error}</Notice> : null}
      <Card>
        <div className="sz-row-between">
          <div className="sz-stack-sm">
            <span className="sz-kicker">Current public IP</span>
            {current?.ipAddress
              ? <>
                  <span className="sz-hero-ip">{current.ipAddress}</span>
                  {current.lastChecked ? <span className="sz-small sz-muted">Last confirmed {formatDateTime(current.lastChecked)}</span> : null}
                </>
              : <span className="sz-muted">No IP check recorded yet</span>}
          </div>
          <Button onClick={() => void checkNow()} disabled={checking}>{checking ? 'Checking…' : 'Check now'}</Button>
        </div>
      </Card>
      <Card>
        <div className="sz-card-header">
          <div className="sz-stack-sm">
            <h2>IP address history</h2>
            <span className="sz-small sz-muted">One row per address, with how many checks confirmed it</span>
          </div>
        </div>
        <DataTable caption="IP address history" density="standard" rows={history ?? []} columns={COLUMNS}
          rowKey={(r) => r.ipAddress} empty="No IP checks recorded yet" />
      </Card>
    </div>
  );
}
