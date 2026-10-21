import { useState } from 'react';
import { api } from '../../api/client';
import type { CloudflareDnsRecord, DnsProvider, DnsRecord, DnsRecordInput } from '../../api/types';
import { RECORD_TYPES, autoUpdateUnavailableReason, supportsAutoUpdate } from '../../app/dns';
import { Notice } from '../../components/Notice';
import { Button, Checkbox, Dialog, Input, Select } from '../../ui';

const WIDE = { maxWidth: 560 };

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
