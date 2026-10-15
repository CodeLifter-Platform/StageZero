/** Local date and time, short: what the operator reads in a table. */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  return date.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
}

/** "just now", "5 min ago", "3 hours ago" … "2 years ago". */
export function relativeTime(iso: string, now: Date = new Date()): string {
  const minutes = (now.getTime() - new Date(iso).getTime()) / 60_000;
  const plural = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'} ago`;
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${Math.floor(minutes)} min ago`;
  const hours = minutes / 60;
  if (hours < 24) return plural(Math.floor(hours), 'hour');
  const days = hours / 24;
  if (days < 7) return plural(Math.floor(days), 'day');
  if (days < 30) return plural(Math.floor(days / 7), 'week');
  if (days < 365) return plural(Math.floor(days / 30), 'month');
  return plural(Math.floor(days / 365), 'year');
}
