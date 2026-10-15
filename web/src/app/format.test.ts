import { describe, expect, it } from 'vitest';
import { relativeTime } from './format';

const now = new Date('2026-10-15T12:00:00Z');
const ago = (ms: number) => new Date(now.getTime() - ms).toISOString();
const MIN = 60_000;

describe('relativeTime', () => {
  it.each([
    [30_000, 'just now'],
    [5 * MIN, '5 min ago'],
    [60 * MIN, '1 hour ago'],
    [3 * 60 * MIN, '3 hours ago'],
    [2 * 24 * 60 * MIN, '2 days ago'],
    [14 * 24 * 60 * MIN, '2 weeks ago'],
    [90 * 24 * 60 * MIN, '3 months ago'],
    [800 * 24 * 60 * MIN, '2 years ago'],
  ])('%i ms ago reads "%s"', (ms, text) => {
    expect(relativeTime(ago(ms), now)).toBe(text);
  });
});
