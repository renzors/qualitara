// Mirrors the API DTOs in src/Relay.Api/Dtos.cs (camelCase JSON, snake_case enum values).

export type Verdict = 'no_activity' | 'insufficient_history' | 'unusual' | 'worth_a_look' | 'normal';
export type MetricStatus = 'normal' | 'worth_a_look' | 'unusual' | 'insufficient_data';
export type MetricKind = 'count' | 'rate' | 'duration';
export type Polarity = 'neutral' | 'good' | 'bad';
export type Direction = 'same' | 'higher' | 'lower';
export type Sensitivity = 'relaxed' | 'normal' | 'strict';
export type WindowDays = 1 | 7 | 30;

export interface Period { start: string; end: string; }

export interface Params { asOf: string; window: WindowDays; sensitivity: Sensitivity; lookAt: number; unusualAt: number; }

export interface MetricDisplay { value: string; low: string | null; high: string | null; expected: string | null; }

export interface Metric {
  key: string;
  label: string;
  kind: MetricKind;
  status: MetricStatus;
  value: number | null;
  expected: number | null;
  expectedLow: number | null;
  expectedHigh: number | null;
  z: number | null;
  direction: Direction;
  polarity: Polarity;
  sampleSize: number;
  sampleUnit: string;
  valueLow: number | null;
  valueHigh: number | null;
  display: MetricDisplay;
  reason: string;
  detail: string | null;
}

export interface Scope {
  verdict: Verdict;
  note: string | null;
  reasons: string[];
  recentTotal: number;
  expectedTotal: number | null;
  metrics: Metric[];
}

export interface Account { id: number; name: string; industry: string; timezone: string; createdAt: string; locationCount: number; }

export interface AccountListItem {
  account: Account;
  verdict: Verdict;
  note: string | null;
  reasons: string[];
  recentTotal: number;
  expectedTotal: number | null;
  metrics: Metric[];
  history: (Verdict | null)[];
  locationVerdicts: Verdict[];
}

export interface AccountList { params: Params; recent: Period; baselinePeriods: number; accounts: AccountListItem[]; }

export interface LocationResult { location: string; isNew: boolean; assessment: Scope; }

export interface DataQuality {
  duplicatesRemoved: number;
  recentEvents: number;
  recentNoOutcome: number;
  recentCalls: number;
  recentCallsNoDuration: number;
  baselineNote: string | null;
}

export interface Assessment {
  account: Account;
  params: Params;
  recent: Period;
  baseline: Period[];
  baselineRequested: number;
  overall: Scope;
  dataQuality: DataQuality;
  rareHours: boolean[];
  rareHoursText: string;
  locations: LocationResult[];
}

export interface SeriesPoint { value: number | null; expected: number | null; low: number | null; high: number | null; z: number | null; status: MetricStatus; display: string; }
export interface MetricSeries { key: string; label: string; kind: MetricKind; points: SeriesPoint[]; }
export interface Week { start: string; end: string; verdict: Verdict; isCurrent: boolean; peakDay: { date: string; count: number } | null; }
export interface Series { params: Params; location: string | null; weeks: Week[]; series: MetricSeries[]; }

export interface Heatmap {
  params: Params;
  location: string | null;
  recent: number[][];
  baselineAverage: number[][];
  rareHours: boolean[];
  rareHoursText: string;
  baselinePeriods: number;
  recentTotal: number;
  recentRare: number;
}

export interface Meta {
  firstEventAt: string;
  lastEventAt: string;
  minAsOf: string;
  maxAsOf: string;
  defaultAsOf: string;
  windows: WindowDays[];
  defaultWindow: WindowDays;
  sensitivities: Sensitivity[];
  defaultSensitivity: Sensitivity;
}

export interface Problem { title?: string; detail?: string; errors?: Record<string, string[]>; }
