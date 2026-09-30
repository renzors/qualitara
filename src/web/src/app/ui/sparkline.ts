import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MetricKind, SeriesPoint } from '../api/models';

const W = 300, H = 40, TOP = 12, BOTTOM = 3;

/**
 * Weekly sparkline for one signal. The shaded band is this week's usual range, drawn across the 8 comparison weeks.
 * Values above the cap are clipped and labelled, so one spike doesn't flatten the rest.
 */
@Component({
  selector: 'app-sparkline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (g(); as g) {
      <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" role="img" [attr.aria-label]="label()">
        @if (g.band) {
          <rect [attr.x]="g.band.x" [attr.y]="g.band.y" [attr.width]="g.band.w" [attr.height]="g.band.h" class="spark-band" />
        }
        @for (seg of g.segments; track $index) {
          <polyline [attr.points]="seg" class="spark-line" />
        }
        @for (c of g.clipped; track c.x) {
          <path [attr.d]="'M' + (c.x - 3) + ',6 l3,-3 l3,3'" class="spark-clip" />
          @if ($first) {
            <text [attr.x]="c.x + 5" y="9" class="spark-clip-label" [attr.text-anchor]="c.x > W - 90 ? 'end' : 'start'"
              [attr.dx]="c.x > W - 90 ? -10 : 0">{{ c.text }}</text>
          }
        }
        @if (g.last) {
          <circle [attr.cx]="g.last.x" [attr.cy]="g.last.y" r="3.5" [style.fill]="dotColor()" />
        }
      </svg>
    } @else {
      <svg [attr.viewBox]="'0 0 ' + W + ' ' + H" aria-hidden="true"></svg>
    }
  `,
})
export class Sparkline {
  protected readonly W = W;
  protected readonly H = H;

  readonly points = input.required<SeriesPoint[]>();
  readonly kind = input<MetricKind>('count');
  readonly dotColor = input('var(--ink)');
  readonly label = input('');

  protected readonly g = computed(() => {
    const pts = this.points();
    const n = pts.length;
    if (n < 2) return null;

    const values = pts.map(p => p.value).filter((v): v is number => v !== null).sort((a, b) => a - b);
    if (!values.length) return null;
    const median = values[values.length >> 1];
    const current = pts[n - 1];
    const cap = Math.max(median * 2.2, (current.high ?? 0) * 1.1, this.kind() === 'rate' ? 0.05 : 1);

    const x = (i: number) => 2 + (i * (W - 4)) / (n - 1);
    const y = (v: number) => H - BOTTOM - (Math.min(Math.max(v, 0), cap) / cap) * (H - TOP - BOTTOM);

    const segments: string[] = [];
    let cur: string[] = [];
    pts.forEach((p, i) => {
      if (p.value === null) {
        if (cur.length > 1) segments.push(cur.join(' '));
        cur = [];
      } else cur.push(`${x(i).toFixed(1)},${y(p.value).toFixed(1)}`);
    });
    if (cur.length > 1) segments.push(cur.join(' '));

    const clipped = pts
      .map((p, i) => ({ v: p.value, i }))
      .filter(p => p.v !== null && p.v > cap)
      .map(p => ({ x: x(p.i), text: this.fmt(p.v!) }));

    let band = null;
    if (current.low !== null && current.high !== null) {
      const from = x(Math.max(0, n - 9)), to = x(n - 2);
      const yh = y(current.high), yl = y(Math.max(0, current.low));
      band = { x: from, y: yh, w: Math.max(1, to - from), h: Math.max(1, yl - yh) };
    }

    const last = current.value !== null ? { x: x(n - 1), y: y(current.value) } : null;
    return { segments, clipped, band, last };
  });

  private fmt(v: number): string {
    return this.kind() === 'rate' ? `${Math.round(v * 100)}%` : this.kind() === 'duration' ? `${Math.round(v / 60)}m` : String(Math.round(v));
  }
}
