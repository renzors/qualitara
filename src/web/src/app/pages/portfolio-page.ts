import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { AccountList, AccountListItem, Verdict } from '../api/models';
import { ViewParams, withQuery } from '../api/view-params';
import { ZCell } from '../ui/z-cell';
import { VerdictBadge } from '../ui/verdict-badge';
import { VERDICT_CODE, VERDICT_LABEL, cityOf, localDate, periodNoun, utcStamp } from '../ui/format';
import { ProblemBox } from '../ui/problem-box';

const COLUMNS = [
  { key: 'total', label: 'All', sep: true },
  { key: 'calls', label: 'Calls' },
  { key: 'leads', label: 'Leads' },
  { key: 'appointments', label: 'Appts' },
  { key: 'missed_rate', label: 'Missed', sep: true },
  { key: 'voicemail_rate', label: 'Voicemail' },
  { key: 'conversion_rate', label: 'Converted' },
  { key: 'no_show_rate', label: 'No-show' },
  { key: 'talk_time', label: 'Talk time', sep: true },
  { key: 'odd_hours', label: 'Odd hours' },
  { key: 'no_outcome', label: 'No outcome', sep: true },
];

@Component({
  selector: 'app-portfolio-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ZCell, VerdictBadge, RouterLink, ProblemBox],
  template: `
    @if (list.error()) {
      <app-problem [error]="list.error()" />
    } @else if (data(); as d) {
      <div class="summary">
        <h1>{{ heading() }}</h1>
        <div class="counts">
          @for (c of counts(); track c.verdict) {
            <span><b>{{ c.n }}</b> {{ c.label }}</span>
          }
        </div>
      </div>

      <div class="key">
        <span>Cells show how far each signal is from usual (robust z-score). ±{{ d.params.lookAt }} = worth a look, ±{{ d.params.unusualAt }} = unusual.</span>
        <span><i class="sw" style="background:color-mix(in srgb,var(--good) 60%,var(--paper))"></i>better than usual</span>
        <span><i class="sw" style="background:color-mix(in srgb,var(--bad) 60%,var(--paper))"></i>worse than usual</span>
        <span><i class="sw" style="background:color-mix(in srgb,var(--neutral) 60%,var(--paper))"></i>different, neither good nor bad</span>
        <span><i class="sw na"></i>· not enough data</span>
      </div>

      <div class="gridwrap" [class.loading]="list.isLoading()">
        <table class="m">
          <thead>
            <tr class="grp">
              <th colspan="2"></th><th class="sep" colspan="4">Volume</th><th class="sep" colspan="4">Outcomes</th>
              <th class="sep" colspan="2">Behaviour</th><th class="sep">Data</th><th class="sep" colspan="2"></th>
            </tr>
            <tr>
              <th>Verdict</th><th>Account</th>
              @for (c of columns; track c.key) {
                <th class="r" [class.sep]="c.sep">{{ c.label }}</th>
              }
              <th class="sep">Last 25 weeks</th><th>Sites</th>
            </tr>
          </thead>
          <tbody>
            @for (a of d.accounts; track a.account.id) {
              <tr class="rowlink" tabindex="0" (click)="open(a)" (keydown.enter)="open(a)" [attr.aria-label]="'Open ' + a.account.name">
                <td><app-verdict [verdict]="a.verdict" /></td>
                <td class="acct">
                  <a [routerLink]="['/accounts', a.account.id]" queryParamsHandling="preserve" (click)="$event.stopPropagation()">{{ a.account.name }}</a>
                  <small>{{ a.account.industry }} · {{ city(a.account.timezone) }}</small>
                </td>
                @for (c of columns; track c.key) {
                  <td [appZCell]="metric(a, c.key)" [sep]="!!c.sep"></td>
                }
                <td class="sep">
                  @if (a.verdict !== 'no_activity') {
                    <span class="hist" [attr.aria-label]="'Weekly verdicts, oldest first'">
                      @for (h of a.history; track $index) { <i [class]="code(h)" [attr.title]="histTitle($index, h)"></i> }
                    </span>
                  } @else { <span class="muted small">no events ever</span> }
                </td>
                <td>
                  <span class="sites">
                    @for (v of a.locationVerdicts; track $index) { <i [class]="code(v)" [attr.title]="label(v)"></i> }
                  </span>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
      <p class="foot">
        Rows are sorted by verdict, then by the largest deviation. Each bar in "Last 25 weeks" is that week's verdict
        (grey normal, amber worth a look, red unusual, outlined = not enough history yet). Each dot in "Sites" is one location's
        verdict for the selected period. Select a row to open the account.
      </p>
    } @else {
      <p class="note">Loading accounts…</p>
    }
  `,
})
export class PortfolioPage {
  private readonly params = inject(ViewParams);
  private readonly router = inject(Router);
  protected readonly columns = COLUMNS;

  protected readonly list = httpResource<AccountList>(() => withQuery('/api/accounts', this.params.apiQuery()));
  // Keep the previous table while a new one loads, so changing a control doesn't blank the page.
  protected readonly data = linkedSignal<AccountList | undefined, AccountList | undefined>({
    source: () => (this.list.hasValue() ? this.list.value() : undefined),
    computation: (v, prev) => v ?? prev?.value,
  });

  protected readonly heading = computed(() => {
    const d = this.data();
    if (!d) return '';
    const end = localDate(d.recent.end);
    const w = d.params.window;
    if (w === 1) return `Day ending ${utcStamp(d.recent.end)}, compared to the same weekday in the 8 weeks before`;
    if (w === 30) return `30 days ending ${end}, compared to the ${d.baselinePeriods} periods before`;
    return `Week ending ${end}, compared to the ${d.baselinePeriods} weeks before`;
  });

  protected readonly counts = computed(() => {
    const d = this.data();
    if (!d) return [];
    const order: Verdict[] = ['unusual', 'worth_a_look', 'normal', 'insufficient_history', 'no_activity'];
    return order
      .map(v => ({ verdict: v, n: d.accounts.filter(a => a.verdict === v).length, label: VERDICT_LABEL[v].toLowerCase() }))
      .filter(c => c.n > 0);
  });

  protected metric(a: AccountListItem, key: string) {
    return a.metrics.find(m => m.key === key);
  }

  protected code(v: Verdict | null): string {
    return v ? VERDICT_CODE[v] : 'x';
  }

  protected label(v: Verdict): string {
    return VERDICT_LABEL[v];
  }

  protected histTitle(i: number, v: Verdict | null): string {
    const ago = 24 - i;
    return `${ago === 0 ? 'This ' + periodNoun(7) : ago + (ago === 1 ? ' week' : ' weeks') + ' earlier'}: ${v ? VERDICT_LABEL[v] : 'no data yet'}`;
  }

  protected city(tz: string): string {
    return cityOf(tz);
  }

  protected open(a: AccountListItem): void {
    void this.router.navigate(['/accounts', a.account.id], { queryParamsHandling: 'preserve' });
  }
}
