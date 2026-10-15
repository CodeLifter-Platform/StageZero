import { Button } from '../ui';
import { useTheme } from './theme';

export function ThemeToggle() {
  const [theme, toggle] = useTheme();
  const next = theme === 'dark' ? 'light' : 'dark';
  return (
    <Button variant="ghost" size="sm" onClick={toggle} aria-label={`Switch to ${next} theme`} title={`Switch to ${next} theme`}>
      {theme === 'dark' ? '☀ Light' : '☾ Dark'}
    </Button>
  );
}
