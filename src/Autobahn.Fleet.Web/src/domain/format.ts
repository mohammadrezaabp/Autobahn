const DASH = '–';

/** GPU memory arrives in MiB; shown in GiB with one decimal under 100. */
export function gib(mib: number | null | undefined): string {
  if (mib === null || mib === undefined) return DASH;
  const value = mib / 1024;
  return value >= 100 ? value.toFixed(0) : value.toFixed(1);
}

export function bytes(value: number | null | undefined): string {
  if (value === null || value === undefined) return DASH;
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
  let n = value;
  let unit = 0;
  while (n >= 1024 && unit < units.length - 1) {
    n /= 1024;
    unit += 1;
  }
  return `${n >= 100 || unit === 0 ? n.toFixed(0) : n.toFixed(1)} ${units[unit]}`;
}

export function whole(value: number | null | undefined, unit: string): string {
  return value === null || value === undefined ? DASH : `${Math.round(value)}${unit}`;
}

/** 0 to 1, or null when either number is missing. */
export function ratio(part: number | null | undefined, total: number | null | undefined): number | null {
  if (part === null || part === undefined || !total) return null;
  return Math.min(1, Math.max(0, part / total));
}

/** A length of time, for "6 min left". */
export function duration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return DASH;
  if (seconds < 60) return `${Math.max(1, Math.round(seconds))} s`;
  if (seconds < 3600) return `${Math.round(seconds / 60)} min`;
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.round((seconds - hours * 3600) / 60);
  if (hours >= 48) return `${Math.round(seconds / 86400)} d`;
  return minutes ? `${hours} h ${minutes} min` : `${hours} h`;
}

export function ago(then: number | null, now: number): string {
  if (then === null) return 'never';
  const seconds = Math.max(0, Math.round(now - then));
  if (seconds < 60) return `${seconds} s ago`;
  if (seconds < 3600) return `${Math.round(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)} h ago`;
  return `${Math.round(seconds / 86400)} d ago`;
}
