import { Metric, MetricStatus, Polarity, Verdict } from '../api/models';

export const VERDICT_LABEL: Record<Verdict, string> = {
  unusual: 'Unusual',
  worth_a_look: 'Worth a look',
  normal: 'Normal',
  insufficient_history: 'Not enough history',
  no_activity: 'No activity',
};

/** Short class used by the verdict strip and site dots. */
export const VERDICT_CODE: Record<Verdict, string> = {
  unusual: 'u',
  worth_a_look: 'l',
  normal: 'n',
  insufficient_history: 'i',
  no_activity: 'x',
};

export function isAssessed(m: Metric | undefined | null): m is Metric & { z: number } {
  return !!m && m.status !== 'insufficient_data' && m.z !== null;
}

export function isFlagged(status: MetricStatus): boolean {
  return status === 'unusual' || status === 'worth_a_look';
}

/** CSS colour for a deviation: blue better, orange worse, violet different-but-neutral. */
export function polarityColor(p: Polarity): string {
  return p === 'good' ? 'var(--good)' : p === 'bad' ? 'var(--bad)' : 'var(--neutral)';
}

export function signed(z: number): string {
  return `${z > 0 ? '+' : z < 0 ? '−' : ''}${Math.abs(z).toFixed(1)}`;
}

/** 1 = inside ±2, 2 = worth a look, 3 = unusual; drives the cell weight. */
export function zLevel(m: Metric): 1 | 2 | 3 {
  return m.status === 'unusual' ? 3 : m.status === 'worth_a_look' ? 2 : 1;
}

/** Cell background: tint grows with |z| from 1 upward, capped at |z| = 4. */
export function cellBackground(m: Metric): string | null {
  if (!isAssessed(m) || Math.abs(m.z) < 1) return null;
  const a = Math.round(Math.min(Math.abs(m.z) / 4, 1) * 62);
  return `color-mix(in srgb, ${polarityColor(m.polarity)} ${a}%, var(--paper))`;
}

export function cellTitle(m: Metric | undefined): string {
  if (!m) return 'Not enough data';
  return m.reason;
}

export function cityOf(tz: string): string {
  if (tz.toUpperCase() === 'UTC') return 'UTC';
  return (tz.split('/').pop() ?? tz).replace(/_/g, ' ');
}

const fmtCache = new Map<string, Intl.DateTimeFormat>();
function fmt(tz: string, opts: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = tz + JSON.stringify(opts);
  let f = fmtCache.get(key);
  if (!f) {
    f = new Intl.DateTimeFormat('en-US', { timeZone: tz, ...opts });
    fmtCache.set(key, f);
  }
  return f;
}

/** "Jul 20, 6:20pm" in the given IANA zone. */
export function localDateTime(iso: string, tz: string): string {
  return fmt(tz, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
    .format(new Date(iso))
    .replace(/\s?(AM|PM)$/, (_, ap: string) => ap.toLowerCase());
}

/** "Jul 27" in the given IANA zone. */
export function localDate(iso: string, tz = 'UTC'): string {
  return fmt(tz, { month: 'short', day: 'numeric' }).format(new Date(iso));
}

/** "Jul 27, 22:20 UTC" */
export function utcStamp(iso: string): string {
  const d = new Date(iso);
  const hh = String(d.getUTCHours()).padStart(2, '0');
  const mm = String(d.getUTCMinutes()).padStart(2, '0');
  return `${localDate(iso)}, ${hh}:${mm} UTC`;
}

export function percent(part: number, whole: number): string {
  if (!whole) return '–';
  const p = (part / whole) * 100;
  return p > 0 && p < 1 ? '<1%' : `${Math.round(p)}%`;
}

export function periodNoun(windowDays: number): string {
  return windowDays === 1 ? 'day' : windowDays === 7 ? 'week' : '30 days';
}
