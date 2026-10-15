import { useState } from 'react';
import { api } from '../../api/client';
import type { CloudflareDnsRecord, CloudflareZone, DnsProvider, DnsRecord, DnsRecordInput } from '../../api/types';
import { RECORD_TYPES, autoUpdateUnavailableReason, supportsAutoUpdate } from '../../app/dns';
import { Notice } from '../../components/Notice';
import { Button, Checkbox, Dialog, Input, Select, Switch } from '../../ui';

const WIDE = { maxWidth: 560 };

// ── Add provider ────────────────────────────────────────────────

export function AddProviderDialog({ open, onClose, onAdded }: {
  open: boolean;
  onClose: () => void;
  onAdded: (provider: DnsProvider) => void;
}) {
  const [name, setName] = useState('');
  const [token, setToken] = useState('');
  const [zones, setZones] = useState<CloudflareZone[]>([]);
  const [zoneId, setZoneId] = useState('');
  const [enabled, setEnabled] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reset = () => { setName(''); setToken(''); setZones([]); setZoneId(''); setEnabled(true); setError(null); };
  const close = () => { reset(); onClose(); };

  const loadZones = async () => {
    setBusy(true);
    setError(null);
    try {
      const found = await api.dns.zones(token.trim());
      setZones(found);
      setZoneId(found[0]?.id ?? '');
      if (found.length === 0) setError('No zones found for this API token.');
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      onAdded(await api.dns.addProvider(name.trim(), token.trim(), zoneId, enabled));
      reset();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={busy ? undefined : close} title="Add Cloudflare provider" style={WIDE}
      footer={<>
        <Button variant="ghost" onClick={close} disabled={busy}>Cancel</Button>
        <Button onClick={() => void submit()} disabled={busy || !name.trim() || !token.trim() || !zoneId}>Add provider</Button>
      </>}>
      <div className="sz-stack">
        <Input label="Provider name" value={name} onChange={(e) => setName(e.target.value)} disabled={busy} />
        <Input label="Cloudflare API token" hint="Stored encrypted; never shown again" type="password" autoComplete="off"
          value={token} onChange={(e) => { setToken(e.target.value); setZones([]); setZoneId(''); }} disabled={busy} />
        {zones.length === 0
          ? <Button variant="outline" size="sm" onClick={() => void loadZones()} disabled={busy || !token.trim()}>{busy ? 'Loading zones…' : 'Load zones'}</Button>
          : <Select label="Zone" value={zoneId} onChange={(e) => setZoneId(e.target.value)} disabled={busy}>
              {zones.map((z) => <option key={z.id} value={z.id}>{z.name}</option>)}
            </Select>}
        <label className="sz-row">
          <Switch checked={enabled} onChange={setEnabled} disabled={busy} />
          <span className="sz-small">Update DNS automatically when the IP changes</span>
        </label>
        {error ? <Notice tone="danger">{error}</Notice> : null}
      </div>
    </Dialog>
  );
}

// ── Add / edit record ───────────────────────────────────────────

interface RecordForm { recordName: string; recordType: string; content: string; recordId: string; autoUpdate: boolean }

const fromRecord = (r?: DnsRecord): RecordForm => ({
  recordName: r?.recordName ?? '',
  recordType: r?.recordType ?? 'A',
  content: r?.content ?? '',
  recordId: r?.recordId ?? '',
  autoUpdate: r?.autoUpdate ?? true,
});

export const toInput = (f: RecordForm): DnsRecordInput => ({
  recordName: f.recordName.trim(),
  recordType: f.recordType,
  autoUpdate: f.autoUpdate && supportsAutoUpdate(f.recordType),
  recordId: f.recordId.trim() || null,
  content: f.recordType === 'CNAME' ? f.content.trim() : null,
});

function RecordFields({ form, setForm, busy, showRecordId }: {
  form: RecordForm;
  setForm: (f: RecordForm) => void;
  busy: boolean;
  showRecordId: boolean;
}) {
  const reason = autoUpdateUnavailableReason(form.recordType);
  return (
    <>
      <Input label="Record name" hint="For example example.com or home.example.com" value={form.recordName}
        onChange={(e) => setForm({ ...form, recordName: e.target.value })} disabled={busy} />
      <Select label="Record type" value={form.recordType} disabled={busy}
        onChange={(e) => setForm({ ...form, recordType: e.target.value, autoUpdate: supportsAutoUpdate(e.target.value) && form.autoUpdate })}>
        {RECORD_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
      </Select>
      {form.recordType === 'CNAME'
        ? <Input label="CNAME target" hint="The name this record points at" value={form.content}
            onChange={(e) => setForm({ ...form, content: e.target.value })} disabled={busy} />
        : null}
      {showRecordId
        ? <Input label="Cloudflare record ID (optional)" value={form.recordId} className="sz-mono"
            onChange={(e) => setForm({ ...form, recordId: e.target.value })} disabled={busy} />
        : null}
      <Checkbox label="Auto-update when the IP changes" checked={form.autoUpdate && supportsAutoUpdate(form.recordType)}
        disabled={busy || !supportsAutoUpdate(form.recordType)} onChange={(e) => setForm({ ...form, autoUpdate: e.target.checked })} />
      {reason ? <span className="sz-xs sz-muted">{reason}</span> : null}
    </>
  );
}

export function AddRecordDialog({ provider, onClose, onSaved }: {
  provider: DnsProvider | null;
  onClose: () => void;
  onSaved: (record: DnsRecord) => void;
}) {
  const [form, setForm] = useState<RecordForm>(fromRecord());
  const [existing, setExisting] = useState<CloudflareDnsRecord[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const close = () => { setForm(fromRecord()); setExisting(null); setError(null); onClose(); };

  const loadExisting = async () => {
    if (!provider) return;
    setBusy(true);
    setError(null);
    try {
      setExisting(await api.dns.cloudflareRecords(provider.id));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const pick = (id: string) => {
    const r = existing?.find((x) => x.id === id);
    if (!r) return;
    setForm({ recordName: r.name, recordType: r.type, content: r.type === 'CNAME' ? r.content : '', recordId: r.id, autoUpdate: supportsAutoUpdate(r.type) });
  };

  const submit = async () => {
    if (!provider) return;
    setBusy(true);
    setError(null);
    try {
      onSaved(await api.dns.addRecord(provider.id, toInput(form)));
      setForm(fromRecord());
      setExisting(null);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={provider !== null} onClose={busy ? undefined : close} title={`Add DNS record${provider ? ` for ${provider.name}` : ''}`} style={WIDE}
      footer={<>
        <Button variant="ghost" onClick={close} disabled={busy}>Cancel</Button>
        <Button onClick={() => void submit()} disabled={busy || !form.recordName.trim() || (form.recordType === 'CNAME' && !form.content.trim())}>Add record</Button>
      </>}>
      <div className="sz-stack">
        {existing === null
          ? <Button variant="outline" size="sm" onClick={() => void loadExisting()} disabled={busy}>{busy ? 'Loading…' : 'Load existing records from Cloudflare'}</Button>
          : existing.length === 0
            ? <span className="sz-small sz-muted">No A, AAAA or CNAME records in this zone.</span>
            : <Select label="Pick an existing record, or enter one below" value={form.recordId} onChange={(e) => pick(e.target.value)} disabled={busy}>
                <option value="">Enter manually</option>
                {existing.map((r) => <option key={r.id} value={r.id}>{`${r.name} (${r.type}) ${r.content}`}</option>)}
              </Select>}
        <RecordFields form={form} setForm={setForm} busy={busy} showRecordId={false} />
        {error ? <Notice tone="danger">{error}</Notice> : null}
      </div>
    </Dialog>
  );
}

export function EditRecordDialog({ record, onClose, onSaved }: {
  record: DnsRecord | null;
  onClose: () => void;
  onSaved: (record: DnsRecord) => void;
}) {
  const [form, setForm] = useState<RecordForm>(fromRecord(record ?? undefined));
  const [editing, setEditing] = useState<number | null>(record?.id ?? null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // A different record opened: start from its values.
  if ((record?.id ?? null) !== editing) {
    setEditing(record?.id ?? null);
    setForm(fromRecord(record ?? undefined));
    setError(null);
  }

  const submit = async () => {
    if (!record) return;
    setBusy(true);
    setError(null);
    try {
      onSaved(await api.dns.updateRecord(record.id, toInput(form)));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={record !== null} onClose={busy ? undefined : onClose} title="Edit DNS record" style={WIDE}
      footer={<>
        <Button variant="ghost" onClick={onClose} disabled={busy}>Cancel</Button>
        <Button onClick={() => void submit()} disabled={busy || !form.recordName.trim()}>Save</Button>
      </>}>
      <div className="sz-stack">
        <RecordFields form={form} setForm={setForm} busy={busy} showRecordId />
        {error ? <Notice tone="danger">{error}</Notice> : null}
      </div>
    </Dialog>
  );
}
