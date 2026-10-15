import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyTheme, storedTheme, THEME_KEY } from './theme';

afterEach(() => vi.restoreAllMocks());

describe('theme', () => {
  it('is dark when nothing, or nonsense, is stored', () => {
    expect(storedTheme()).toBe('dark');
    localStorage.setItem(THEME_KEY, 'purple');
    expect(storedTheme()).toBe('dark');
  });

  it('swaps the root attribute and remembers the choice', () => {
    applyTheme('light');

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(storedTheme()).toBe('light');
  });

  it('still works when storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked'); });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked'); });

    expect(storedTheme()).toBe('dark');
    expect(() => applyTheme('light')).not.toThrow();
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });
});
