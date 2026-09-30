import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Period, SeriesPoint, Week } from '../api/models';
import { localDate } from './format';

const W = 1000, H = 260, L = 48, R = 18, T = 18, B = 52;
const PLOT_BOTTOM = H - B;
const TRACK_Y = H - 12;
const NICE = [1, 2, 3, 5, 10, 20, 30, 50, 100, 200, 300, 500, 1000, 2000, 3000, 5000, 10000, 20000, 50000];

interface Mark { i: number; x: number; y: number; cls: string; title: string; annotation: string | null; }

/**
 * Events per week across the whole history (from design D, restyled for the Scorecard).
 * Log y-axis so a spike stays readable; the shaded band is the usual range the engine would have used that week.
 * Clicking a week sets "as of" to that week's end.
 */
@Component({
  selector: 'app-weekly-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (g(); as g) {
      <svg class="weekly" [attr.viewBox]="'0 0 ' + W + ' ' + H" role="img"
        [attr.aria-label]="'Events per week, ' + g.first + ' to ' + g.lastLabel + ', log scale'">
        @for (t of g.ticks; track t.v) {
          <line [attr.x1]="L" [attr.x2]="W - R" [attr.y1]="t.y" [attr.y2]="t.y" class="grid" />
          <text [attr.x]="L - 8" [attr.y]="t.y + 4" text-anchor="end" class="axis-label">{{ t.label }}</text>
        }
        @for (p of g.band; track $index) {
          <path [attr.d]="p" class="band" />
        }
        @if (g.current; as c) {
          <rect [attr.x]="c.x - g.step / 2" [attr.y]="T" [attr.width]="Math.min(g.step, W - R + 6 - (c.x - g.step / 2))" [attr.height]="PLOT_BOTTOM - T" class="current-col" />
        }
        <polyline [attr.points]="g.line" class="line" />
        @for (m of g.marks; track m.i) {
          <circle [attr.cx]="m.x" [attr.cy]="m.y" [attr.r]="m.cls === 'now' ? 5 : m.cls === 'pt' ? 2.5 : 4" [attr.class]="'pt ' + m.cls" />
          @if (m.annotation) {
            <text [attr.x]="m.x + (m.x > W - 220 ? -10 : 10)" [attr.y]="m.y + 4" class="annot"
              [attr.text-anchor]="m.x > W - 220 ? 'end' : 'start'">{{ m.annotation }}</text>
          }
        }
        @for (l of g.xLabels; track l.i) {
          <text [attr.x]="l.x" [attr.y]="PLOT_BOTTOM + 18" [attr.text-anchor]="l.anchor" class="axis-label">{{ l.text }}</text>
        }
        <rect [attr.x]="L" [attr.y]="TRACK_Y" [attr.width]="W - L - R" height="3" class="track" />
        @if (g.baseSpan; as s) {
          <rect [attr.x]="s.x" [attr.y]="TRACK_Y" [attr.width]="s.w" height="3" class="track-base" />
        }
        @if (g.recentSpan; as s) {
          <rect [attr.x]="s.x" [attr.y]="TRACK_Y" [attr.width]="s.w" height="3" class="track-now" />
          <rect [attr.x]="s.x + s.w - 2" [attr.y]="TRACK_Y - 6" width="4" height="15" rx="1" class="track-now" />
        }
        @for (m of g.marks; track m.i) {
          <rect class="hit" [attr.x]="m.x - g.step / 2" [attr.y]="T" [attr.width]="g.step" [attr.height]="H - T"
            tabindex="0" role="button" [attr.aria-label]="m.title"
            (click)="pick.emit(g.ends[m.i])" (keydown.enter)="pick.emit(g.ends[m.i])" (keydown.space)="pick.emit(g.ends[m.i]); $event.preventDefault()">
            <title>{{ m.title }}</title>
          </rect>
        }
      </svg>
    } @else {
      <p class="note">No weekly history to chart yet.</p>
    }
  `,
})
export class WeeklyChart {
  protected readonly W = W;
  protected readonly H = H;
  protected readonly L = L;
  protected readonly R = R;
  protected readonly T = T;
  protected readonly PLOT_BOTTOM = PLOT_BOTTOM;
  protected readonly TRACK_Y = TRACK_Y;
  protected readonly Math = Math;

  readonly weeks = input.required<Week[]>();
  readonly points = input.required<SeriesPoint[]>();
  readonly recent = input<Period | null>(null);
  readonly baseline = input<Period[]>([]);
  readonly timezone = input('UTC');
  readonly pick = output<string>();

  protected readonly g = computed(() => {
    const weeks = this.weeks(), pts = this.points(), tz = this.timezone();
    const n = weeks.length;
    if (n < 2 || pts.length !== n) return null;

    // Log domain from the data and bands, snapped outward to nice values.
    const vals: number[] = [];
    pts.forEach(p => {
      if (p.value !== null && p.value > 0) vals.push(p.value);
      if (p.high !== null && p.high > 0) vals.push(p.high);
      if (p.low !== null && p.low > 0) vals.push(p.low);
    });
    if (!vals.length) vals.push(1);
    const lo = Math.max(1, [...NICE].reverse().find(v => v <= Math.min(...vals) * 0.9) ?? 1);
    const hi = NICE.find(v => v >= Math.max(...vals) * 1.1) ?? Math.max(...vals) * 1.2;
    const l0 = Math.log10(lo), l1 = Math.log10(hi);

    const x = (i: number) => L + (i * (W - L - R)) / (n - 1);
    const y = (v: number) => T + (1 - (Math.log10(Math.max(v, lo)) - l0) / (l1 - l0)) * (PLOT_BOTTOM - T);
    const step = (W - L - R) / (n - 1);

    let ticks = NICE.filter(v => v >= lo && v <= hi);
    if (ticks.length > 6) ticks = ticks.filter(v => /^[13]/.test(String(v)) || v === lo || v === hi);
    const tickObjs = ticks.map(v => ({ v, y: y(v), label: v >= 1000 ? `${v / 1000}k` : String(v) }));

    // Band: contiguous runs of weeks that had enough history.
    const band: string[] = [];
    let run: number[] = [];
    const flush = () => {
      if (run.length > 1) {
        const top = run.map(i => `${x(i).toFixed(1)},${y(pts[i].high!).toFixed(1)}`);
        const bot = run.slice().reverse().map(i => `${x(i).toFixed(1)},${y(Math.max(pts[i].low!, lo)).toFixed(1)}`);
        band.push(`M${top.join(' L')} L${bot.join(' L')} Z`);
      }
      run = [];
    };
    pts.forEach((p, i) => (p.low !== null && p.high !== null ? run.push(i) : flush()));
    flush();

    const line = pts.map((p, i) => `${x(i).toFixed(1)},${y(p.value ?? 0).toFixed(1)}`).join(' ');

    // Annotate at most the three most extreme unusual weeks.
    const unusual = pts.map((p, i) => ({ p, i })).filter(o => o.p.status === 'unusual')
      .sort((a, b) => Math.abs(b.p.z ?? 0) - Math.abs(a.p.z ?? 0)).slice(0, 3).map(o => o.i);

    const marks: Mark[] = pts.map((p, i) => {
      const w = weeks[i];
      const v = p.value ?? 0;
      let annotation: string | null = null;
      if (unusual.includes(i)) {
        const peak = w.peakDay;
        annotation = peak && peak.count * 2 >= v ? `${v} · incl. ${peak.count} on ${localDate(peak.date + 'T12:00:00Z')}` : `${v}`;
      }
      const cls = w.isCurrent ? 'now' : p.status === 'unusual' ? 'unusual' : p.status === 'worth_a_look' ? 'look' : 'pt';
      const usual = p.low !== null && p.high !== null ? `, usual ${Math.max(0, Math.ceil(p.low))}–${Math.floor(p.high)}` : ', not enough history';
      const title = `Week ending ${localDate(w.end, tz)}: ${v} events${usual}. Select to view as of this week.`;
      return { i, x: x(i), y: y(v), cls, title, annotation };
    });

    // Month labels at the first week ending in each month, thinned so they never collide.
    const xLabels: { i: number; x: number; text: string; anchor: string }[] = [];
    let lastMonth = '', lastX = -Infinity;
    weeks.forEach((w, i) => {
      const d = new Date(w.end);
      const month = `${d.getUTCFullYear()}-${d.getUTCMonth()}`;
      if (month !== lastMonth && x(i) - lastX > 70 && x(i) < W - R - 60) {
        xLabels.push({ i, x: x(i), text: localDate(w.end, tz), anchor: 'middle' });
        lastX = x(i);
      }
      lastMonth = month;
    });
    xLabels.push({ i: n - 1, x: x(n - 1), text: localDate(weeks[n - 1].end, tz), anchor: 'end' });

    // Track: time-proportional spans for the comparison periods and the recent window.
    const t0 = Date.parse(weeks[0].end), t1 = Date.parse(weeks[n - 1].end);
    const tx = (iso: string) => L + ((Math.min(Math.max(Date.parse(iso), t0 - 7 * 864e5), t1) - t0) / (t1 - t0)) * (W - L - R);
    const span = (a: string, b: string) => {
      const x0 = Math.max(L, tx(a)), x1 = Math.min(W - R, tx(b));
      return x1 > x0 ? { x: x0, w: Math.max(2, x1 - x0) } : null;
    };
    const base = this.baseline();
    const baseSpan = base.length ? span(base[base.length - 1].start, base[0].end) : null;
    const rec = this.recent();
    const recentSpan = rec ? span(rec.start, rec.end) : null;

    const currentIdx = weeks.findIndex(w => w.isCurrent);
    return {
      ticks: tickObjs, band, line, marks, xLabels, step, baseSpan, recentSpan,
      current: currentIdx >= 0 ? { x: x(currentIdx) } : null,
      ends: weeks.map(w => w.end),
      first: localDate(weeks[0].end, tz),
      lastLabel: localDate(weeks[n - 1].end, tz),
    };
  });
}
