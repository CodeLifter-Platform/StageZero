import { describe, expect, it } from 'vitest';
import type { DnsRecord } from '../api/types';
import { groupRecords, importSummary } from './dns';

let next = 1;
const rec = (recordName: string, recordType: string, content: string | null = null): DnsRecord => ({
  id: next++, providerId: 1, recordName, recordType, content, recordId: null, autoUpdate: recordType === 'A',
  autoUpdateSupported: recordType === 'A', autoUpdateUnavailableReason: null, lastIpAddress: null, lastUpdatedAt: null,
});

describe('groupRecords', () => {
  const home = rec('home.example.com', 'A');
  const v6 = rec('home.example.com', 'AAAA');
  const apex = rec('example.com', 'A');
  const www = rec('www.example.com', 'CNAME', 'home.example.com.');
  const app = rec('app.example.com', 'CNAME', 'home.example.com');
  const tunnel = rec('ssh.example.com', 'CNAME', 'abc.cfargotunnel.com');
  const all = [www, tunnel, home, app, apex, v6];

  it('puts each CNAME under the address it points at, trailing dot or not', () => {
    const rows = groupRecords(all, false).map((g) => `${g.nested ? '  ' : ''}${g.record.recordName} ${g.record.recordType}`);

    expect(rows).toEqual([
      'example.com A',
      'home.example.com A',
      '  app.example.com CNAME',
      '  www.example.com CNAME',
      'home.example.com AAAA',
    ]);
  });

  it('shows CNAMEs pointing elsewhere only when asked, at the end', () => {
    expect(groupRecords(all, false).some((g) => g.record === tunnel)).toBe(false);
    expect(groupRecords(all, true).at(-1)).toEqual({ record: tunnel, nested: false });
  });

  it('lists a CNAME once even when an A and an AAAA share its target', () => {
    const rows = groupRecords(all, true).filter((g) => g.record === www);
    expect(rows).toHaveLength(1);
  });
});

describe('importSummary', () => {
  it.each([
    [2, 1, 'Imported 2 new record(s), skipped 1 duplicate(s)'],
    [3, 0, 'Imported 3 record(s)'],
    [0, 4, 'All 4 record(s) already exist, nothing new imported'],
    [0, 0, 'No records found to import'],
  ])('%i imported, %i skipped', (imported, skipped, message) => {
    expect(importSummary(imported, skipped).message).toBe(message);
  });
});
