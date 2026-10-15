import { useCallback, useState } from 'react';

export type Theme = 'dark' | 'light';

export const THEME_KEY = 'stagezero.theme';

/** The stored choice; dark (the canonical CodeLifter theme) when absent, unreadable or junk. */
export function storedTheme(): Theme {
  try {
    return localStorage.getItem(THEME_KEY) === 'light' ? 'light' : 'dark';
  } catch {
    return 'dark';
  }
}

/** Swaps every --cl-* token in one step (a root attribute) and remembers the choice. */
export function applyTheme(theme: Theme): void {
  document.documentElement.setAttribute('data-theme', theme);
  try {
    localStorage.setItem(THEME_KEY, theme);
  } catch {
    // Private window or blocked storage: the toggle still works for this visit.
  }
}

export function useTheme(): [Theme, () => void] {
  const [theme, setTheme] = useState<Theme>(() => storedTheme());
  const toggle = useCallback(() => {
    setTheme((current) => {
      const next: Theme = current === 'dark' ? 'light' : 'dark';
      applyTheme(next);
      return next;
    });
  }, []);
  return [theme, toggle];
}
