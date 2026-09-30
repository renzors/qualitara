import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Metric } from '../api/models';
import { cellBackground, cellTitle, isAssessed, polarityColor, signed, zLevel } from './format';

/** One matrix cell: recent value plus signed distance from usual, tinted by polarity and |z|. */
@Component({
  selector: 'td[appZCell]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'z',
    '[class.na]': '!assessed()',
    '[class.sep]': 'sep()',
    '[attr.data-l]': 'level()',
    '[style.--pc]': 'color()',
    '[style.background]': 'bg()',
    '[attr.title]': 'title()',
  },
  template: `
    @if (assessed()) {
      @if (showValue()) { <span class="v">{{ metric()!.display.value }}</span> }{{ z() }}
    } @else {
      ·
    }
  `,
})
export class ZCell {
  readonly metric = input<Metric | undefined>(undefined, { alias: 'appZCell' });
  readonly sep = input(false);
  readonly showValue = input(true);

  protected readonly assessed = computed(() => isAssessed(this.metric()));
  protected readonly level = computed(() => (this.assessed() ? zLevel(this.metric()!) : null));
  protected readonly color = computed(() => (this.assessed() ? polarityColor(this.metric()!.polarity) : null));
  protected readonly bg = computed(() => (this.assessed() ? cellBackground(this.metric()!) : null));
  protected readonly title = computed(() => cellTitle(this.metric()));
  protected readonly z = computed(() => signed(this.metric()?.z ?? 0));
}
