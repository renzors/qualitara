import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Heatmap } from '../api/models';

const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const ORDER = [1, 2, 3, 4, 5, 6, 0]; // API rows are indexed by DayOfWeek (Sunday = 0)

/** Weekday × local hour: recent events; dashed columns are the account's unusual hours. */
@Component({
  selector: 'app-hour-heatmap',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="heat-wrap">
      <div class="heat" role="table" aria-label="Recent events by local weekday and hour">
        <span></span>
        @for (h of hours; track h) {
          <span class="hh">{{ h % 6 === 0 ? label(h) : '' }}</span>
        }
        @for (row of rows(); track row.day) {
          <span class="hd">{{ row.day }}</span>
          @for (c of row.cells; track $index) {
            <span class="c" [class.rare]="c.rare" [style.background]="c.bg" [attr.title]="c.title"></span>
          }
        }
      </div>
    </div>
  `,
})
export class HourHeatmap {
  readonly data = input.required<Heatmap>();
  protected readonly hours = Array.from({ length: 24 }, (_, i) => i);

  protected readonly rows = computed(() => {
    const d = this.data();
    const max = Math.max(1, ...d.recent.flat());
    return ORDER.map((dow, r) => ({
      day: DAYS[r],
      cells: d.recent[dow].map((count, h) => ({
        rare: d.rareHours[h],
        bg: count ? `color-mix(in srgb, var(--accent) ${Math.round(18 + (82 * count) / max)}%, var(--wash))` : null,
        title: `${DAYS[r]} ${this.label(h)}–${this.label((h + 1) % 24)}: ${count} now, usually ${d.baselineAverage[dow][h].toFixed(1)}${d.rareHours[h] ? ' · unusual hour' : ''}`,
      })),
    }));
  });

  protected label(h: number): string {
    return h === 0 ? '12am' : h === 12 ? '12pm' : h < 12 ? `${h}am` : `${h - 12}pm`;
  }
}
