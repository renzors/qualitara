import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Metric } from '../api/models';
import { isAssessed, polarityColor, signed } from './format';

/** Distance-from-usual bar: centre line = usual, ticks at the sensitivity's look/unusual thresholds. */
@Component({
  selector: 'app-z-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (bar(); as b) {
      <div class="zbar" [style.--pc]="b.color" role="img" [attr.aria-label]="'Distance from usual ' + b.label">
        <span class="axis"></span>
        @for (t of b.ticks; track t.pos) {
          <span class="tick" [class.t3]="t.strong" [style.left.%]="t.pos"></span>
        }
        <span class="tick centre" style="left:50%"></span>
        <span class="fill" [style.left.%]="b.left" [style.width.%]="b.width" [class.faint]="!b.flagged"></span>
        <span class="lab" [class.inside]="b.inside" [style.left]="b.labLeft" [style.right]="b.labRight">{{ b.label }}</span>
      </div>
    } @else {
      <span class="na-note">{{ naText() }}</span>
    }
  `,
})
export class ZBar {
  readonly metric = input.required<Metric>();
  readonly lookAt = input(2);
  readonly unusualAt = input(3);

  protected readonly naText = computed(() => {
    const r = this.metric().reason;
    const i = r.indexOf('not assessed');
    return i >= 0 ? r.slice(i).replace(/^not assessed \((.*)\)$/, 'Not assessed: $1') : 'Not assessed';
  });

  protected readonly bar = computed(() => {
    const m = this.metric();
    if (!isAssessed(m)) return null;
    const pct = (z: number) => 50 + Math.max(-5, Math.min(5, z)) * 10;
    const zp = pct(m.z);
    const look = this.lookAt(), unusual = this.unusualAt();
    return {
      color: polarityColor(m.polarity),
      flagged: m.status === 'unusual' || m.status === 'worth_a_look',
      left: Math.min(50, zp),
      width: Math.abs(zp - 50),
      label: signed(m.z),
      // Past the ±4 mark the label sits inside the bar end, so large values never overflow the column.
      inside: Math.abs(m.z) > 4,
      labLeft: Math.abs(m.z) > 4 ? (m.z < 0 ? `calc(${zp}% + 4px)` : null) : m.z >= 0 ? `calc(${zp}% + 4px)` : null,
      labRight: Math.abs(m.z) > 4 ? (m.z > 0 ? `calc(${100 - zp}% + 4px)` : null) : m.z < 0 ? `calc(${100 - zp}% + 4px)` : null,
      ticks: [-unusual, -look, look, unusual].map(t => ({ pos: pct(t), strong: Math.abs(t) === unusual })),
    };
  });
}
