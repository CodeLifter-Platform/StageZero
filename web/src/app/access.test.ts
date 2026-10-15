import { describe, expect, it } from 'vitest';
import { accessFields, joinLines, splitLines } from './access';

describe('accessFields', () => {
  it.each([
    ['none', { session: false, identity: false, serviceToken: false }],
    ['identity', { session: true, identity: true, serviceToken: false }],
    ['service_token', { session: true, identity: false, serviceToken: true }],
    ['both', { session: true, identity: true, serviceToken: true }],
  ] as const)('%s', (mode, fields) => {
    expect(accessFields(mode)).toEqual(fields);
  });
});

describe('identity provider lists', () => {
  it('round-trip one per line, and an empty pick means every provider (null)', () => {
    expect(splitLines(' idp-1 \n\nidp-2\n')).toEqual(['idp-1', 'idp-2']);
    expect(joinLines(['idp-1', 'idp-2'])).toBe('idp-1\nidp-2');
    expect(joinLines([])).toBeNull();
  });
});
