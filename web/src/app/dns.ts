import type { DnsRecord } from '../api/types';

export const RECORD_TYPES = [
  { value: 'A', label: 'A (IPv4)' },
  { value: 'AAAA', label: 'AAAA (IPv6)' },
  { value: 'CNAME', label: 'CNAME (alias)' },
];

/** Mirrors DnsRecord.SupportsAutoUpdate on the server, which enforces it: only A records. */
export const supportsAutoUpdate = (type: string) => type === 'A';

export function autoUpdateUnavailableReason(type: string): string | null {
  switch (type) {
    case 'A': return null;
    case 'AAAA': return "StageZero detects your IPv4 address only, so AAAA records aren't auto-updated.";
    case 'CNAME': return "A CNAME points at a name, not an IP address, so it isn't auto-updated.";
    default: return 'Only A records are auto-updated.';
  }
}

export interface GroupedRecord { record: DnsRecord; nested: boolean }

const bare = (name: string) => name.replace(/\.$/, '');

/**
 * The records table order: each A/AAAA record by name, followed by the CNAMEs that point at
 * it (nested). CNAMEs pointing anywhere else appear only when `showAllCnames` is on, at the end.
 */
export function groupRecords(records: DnsRecord[], showAllCnames: boolean): GroupedRecord[] {
  const byName = (a: DnsRecord, b: DnsRecord) => a.recordName.localeCompare(b.recordName);
  const addresses = records.filter((r) => r.recordType === 'A' || r.recordType === 'AAAA').sort(byName);
  const cnames = records.filter((r) => r.recordType === 'CNAME').sort(byName);

  const result: GroupedRecord[] = [];
  const placed = new Set<number>();
  for (const address of addresses) {
    result.push({ record: address, nested: false });
    for (const cname of cnames) {
      if (cname.content && bare(cname.content) === bare(address.recordName) && !placed.has(cname.id)) {
        result.push({ record: cname, nested: true });
        placed.add(cname.id);
      }
    }
  }
  if (showAllCnames) {
    for (const cname of cnames) {
      if (!placed.has(cname.id)) result.push({ record: cname, nested: false });
    }
  }
  return result;
}

export function importSummary(imported: number, skipped: number): { message: string; tone: 'success' | 'info' } {
  if (imported > 0 && skipped > 0) return { message: `Imported ${imported} new record(s), skipped ${skipped} duplicate(s)`, tone: 'success' };
  if (imported > 0) return { message: `Imported ${imported} record(s)`, tone: 'success' };
  if (skipped > 0) return { message: `All ${skipped} record(s) already exist, nothing new imported`, tone: 'info' };
  return { message: 'No records found to import', tone: 'info' };
}
