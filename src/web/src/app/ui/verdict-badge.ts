import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Verdict } from '../api/models';
import { VERDICT_LABEL } from './format';

@Component({
  selector: 'app-verdict',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'verdict', '[attr.data-v]': 'verdict()' },
  template: `{{ label() }}`,
})
export class VerdictBadge {
  readonly verdict = input.required<Verdict>();
  protected readonly label = computed(() => VERDICT_LABEL[this.verdict()]);
}
