import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Assessment, Heatmap, LocationResult, Metric, MetricSeries, Series } from '../api/models';
import { ViewParams, withQuery } from '../api/view-params';
import { Crumbs } from '../ui/crumbs';
import { HourHeatmap } from '../ui/hour-heatmap';
import { ProblemBox } from '../ui/problem-box';
import { Sparkline } from '../ui/sparkline';
import { VerdictBadge } from '../ui/verdict-badge';
import { WeeklyChart } from '../ui/weekly-chart';
import { ZBar } from '../ui/z-bar';
import { ZCell } from '../ui/z-cell';
import { cityOf, isFlagged, localDateTime, percent, periodNoun, polarityColor } from '../ui/format';

const SITE_COLUMNS = [
  { key: 'total', label: 'All' },
  { key: 'calls', label: 'Calls' },
  { key: 'leads', label: 'Leads' },
  { key: 'appointments', label: 'Appts' },
  { key: 'missed_rate', label: 'Missed', sep: true, rate: true },
  { key: 'conversion_rate', label: 'Converted', rate: true },
  { key: 'no_show_rate', label: 'No-show', rate: true },
  { key: 'odd_hours', label: 'Odd hours', sep: true },
];

@Component({
  selector: 'app-account-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, VerdictBadge, WeeklyChart, Sparkline, ZBar, ZCell, HourHeatmap, ProblemBox],
  template: `
    @if (assessment.error()) {
      <app-problem [error]="assessment.error()" />
      <p><a routerLink="/accounts" queryParamsHandling="preserve">Back to all accounts</a></p>
    } @else if (a(); as a) {
      <div class="summary">
        <app-verdict [verdict]="a.overall.verdict" />
        <h1>{{ a.account.name }}</h1>
        <div class="counts">
          <span>{{ a.account.industry }}</span>
          <span><b>{{ a.account.locationCount }}</b> {{ a.account.locationCount === 1 ? 'location' : 'locations' }}</span>
          <span>{{ city() }} time · {{ recentLabel() }}</span>
        </div>
      </div>

      @if (a.overall.reasons.length || a.overall.note) {
        <ul class="reasons">
          @for (r of a.overall.reasons; track r) { <li>{{ r }}</li> }
          @if (a.overall.note) { <li class="muted">{{ a.overall.note }}</li> }
        </ul>
      }

      <section class="panel-chart" [class.loading]="series.isLoading()">
        <h2>Events per week <small>log scale · shaded band = usual range at the time · click a week to check that date</small></h2>
        @if (s(); as s) {
          <app-weekly-chart [weeks]="s.weeks" [points]="seriesFor('total')?.points ?? []" [recent]="a.recent" [baseline]="a.baseline"
            [timezone]="a.account.timezone" (pick)="pickWeek($event)" />
          <div class="chart-meta">
            <span>Comparison <b>{{ baselineLabel() }}</b></span>
            <span>Window <b>{{ recentLabel() }}</b></span>
            @if (params.asOf()) { <button type="button" class="linkbtn" (click)="params.set({ asOf: null })">Back to latest</button> }
          </div>
        }
      </section>

      <section>
        <h2>Signals <small>weekly history · shaded band = this week's usual range across the comparison weeks · dot = this week</small></h2>
        <div class="ledger">
          <div class="lrow h"><span>Signal</span><span>Last 25 weeks</span><span>This {{ noun() }}</span><span>Usual</span><span>Distance from usual</span></div>
          @for (m of a.overall.metrics; track m.key) {
            <div class="lrow" [class.flagged]="flagged(m)">
              <span class="name">{{ m.label }}<small>{{ sample(m) }}</small></span>
              <app-sparkline [points]="spark(m.key)" [kind]="m.kind" [dotColor]="dot(m)" [label]="m.label + ', last 25 weeks'" />
              <span class="big">{{ m.display.value }}</span>
              <span class="range mono">
                @if (m.display.low !== null) { {{ m.display.low }} – {{ m.display.high }} } @else { – }
                @if (m.display.expected) { <br><span class="small">median {{ m.display.expected }}</span> }
              </span>
              <app-z-bar [metric]="m" [lookAt]="a.params.lookAt" [unusualAt]="a.params.unusualAt" />
            </div>
          }
        </div>
      </section>

      <div class="cols">
        <section>
          <h2>Locations × signals <small>{{ siteNote() }}</small></h2>
          @if (a.locations.length) {
            <div class="gridwrap">
              <table class="m">
                <thead><tr>
                  <th>Site</th><th>Verdict</th>
                  @for (c of siteColumns(); track c.key) { <th class="r" [class.sep]="!!c.sep">{{ c.label }}</th> }
                </tr></thead>
                <tbody>
                  @for (l of a.locations; track l.location) {
                    <tr class="rowlink" tabindex="0" [attr.aria-expanded]="open() === l.location"
                      (click)="toggle(l)" (keydown.enter)="toggle(l)">
                      <td class="acct">{{ l.location }}<small>{{ l.assessment.recentTotal }} events</small></td>
                      <td><app-verdict [verdict]="l.assessment.verdict" /></td>
                      @for (c of siteColumns(); track c.key) {
                        <td [appZCell]="siteMetric(l, c.key)" [sep]="!!c.sep" [showValue]="false"></td>
                      }
                    </tr>
                    @if (open() === l.location) {
                      <tr class="detail"><td [attr.colspan]="2 + siteColumns().length">
                        @for (r of l.assessment.reasons; track r) { <div>{{ r }}</div> }
                        @if (l.assessment.note) { <div class="muted">{{ l.assessment.note }}</div> }
                        @if (!l.assessment.reasons.length && !l.assessment.note) { <div class="muted">All assessed signals are within their usual range.</div> }
                      </td></tr>
                    }
                  }
                </tbody>
              </table>
            </div>
          } @else {
            <p class="note">No location had activity in this period or the comparison periods.</p>
          }
        </section>

        <section>
          <h2>Data quality</h2>
          <dl class="dq">
            <dt>Duplicates removed</dt><dt>No outcome</dt><dt>No call duration</dt>
            <dd>{{ a.dataQuality.duplicatesRemoved }}</dd>
            <dd>{{ pct(a.dataQuality.recentNoOutcome, a.dataQuality.recentEvents) }} of events</dd>
            <dd>{{ pct(a.dataQuality.recentCallsNoDuration, a.dataQuality.recentCalls) }} of calls</dd>
          </dl>
          <p class="note">
            Duplicates are identical rows, counted once everywhere (all-time count). Shares are for this {{ noun() }}.
            Events with no outcome are left out of outcome rates.
          </p>
          @if (a.dataQuality.baselineNote) { <p class="note">{{ a.dataQuality.baselineNote }}</p> }

          <h2 class="gap">Unusual hours <small>{{ a.rareHoursText ? a.rareHoursText + ' ' + city() + ' time' : 'none yet' }}</small></h2>
          @if (oddHours(); as o) { <p class="note">{{ oddHoursText() }}</p> }
          @if (heat(); as h) { <app-hour-heatmap [data]="h" /> }
        </section>
      </div>
    } @else {
      <p class="note">Loading account…</p>
    }
  `,
})
export class AccountPage {
  protected readonly params = inject(ViewParams);
  private readonly crumbs = inject(Crumbs);

  /** Route parameter (bound via withComponentInputBinding). */
  readonly id = input.required<string>();

  protected readonly assessment = httpResource<Assessment>(() => withQuery(`/api/accounts/${this.id()}/assessment`, this.params.apiQuery()));
  protected readonly series = httpResource<Series>(() => withQuery(`/api/accounts/${this.id()}/series`, this.params.apiQuery()));
  protected readonly heatmap = httpResource<Heatmap>(() => withQuery(`/api/accounts/${this.id()}/heatmap`, this.params.apiQuery()));

  // Keep showing the previous result while a new one loads, so controls don't blank the page.
  protected readonly a = linkedSignal<Assessment | undefined, Assessment | undefined>({
    source: () => (this.assessment.hasValue() ? this.assessment.value() : undefined),
    computation: (v, prev) => v ?? prev?.value,
  });
  protected readonly s = linkedSignal<Series | undefined, Series | undefined>({
    source: () => (this.series.hasValue() ? this.series.value() : undefined),
    computation: (v, prev) => v ?? prev?.value,
  });
  protected readonly heat = computed(() => (this.heatmap.hasValue() ? this.heatmap.value() : undefined));

  protected readonly open = signal<string | null>(null);

  constructor() {
    this.crumbs.bind(computed(() => this.a()?.account.name ?? null));
  }

  protected readonly city = computed(() => cityOf(this.a()?.account.timezone ?? 'UTC'));
  protected readonly noun = computed(() => periodNoun(this.a()?.params.window ?? 7));

  protected readonly recentLabel = computed(() => {
    const a = this.a();
    if (!a) return '';
    const tz = a.account.timezone;
    return `${localDateTime(a.recent.start, tz)} – ${localDateTime(a.recent.end, tz)}`;
  });

  protected readonly baselineLabel = computed(() => {
    const a = this.a();
    if (!a || !a.baseline.length) return 'none available';
    const tz = a.account.timezone;
    const from = localDateTime(a.baseline[a.baseline.length - 1].start, tz).split(',')[0];
    const to = localDateTime(a.baseline[0].end, tz).split(',')[0];
    const what = a.params.window === 1 ? 'same weekday' : a.params.window === 7 ? 'weeks' : 'periods';
    return `${from} → ${to} (${a.baseline.length} ${what})`;
  });

  /** Outcome-rate columns are dropped when no site reaches the sample minimum. */
  protected readonly siteColumns = computed(() => {
    const locs = this.a()?.locations ?? [];
    return SITE_COLUMNS.filter(c => !c.rate || locs.some(l => l.assessment.metrics.some(m => m.key === c.key && m.status !== 'insufficient_data')));
  });

  protected readonly siteNote = computed(() => {
    const hidden = SITE_COLUMNS.filter(c => c.rate).length - this.siteColumns().filter(c => c.rate).length;
    return hidden ? 'outcome rates need 10+ events per site, so columns without any are hidden' : 'select a site for its reasons';
  });

  protected readonly oddHours = computed(() => this.a()?.overall.metrics.find(m => m.key === 'odd_hours'));

  protected readonly oddHoursText = computed(() => {
    const h = this.heat(), m = this.oddHours();
    if (!h || !m) return '';
    const usual = m.display.expected ? ` Usually ${m.display.expected}.` : '';
    return `${h.recentRare} of ${h.recentTotal} events this ${this.noun()} (${percent(h.recentRare, h.recentTotal)}).${usual}`;
  });

  protected seriesFor(key: string): MetricSeries | undefined {
    return this.s()?.series.find(x => x.key === key);
  }

  /** The 25 weeks ending at the current week (series may continue past a replayed as-of). */
  protected spark(key: string) {
    const s = this.s();
    const points = this.seriesFor(key)?.points ?? [];
    if (!s) return [];
    const cur = s.weeks.findIndex(w => w.isCurrent);
    const end = cur >= 0 ? cur + 1 : points.length;
    return points.slice(Math.max(0, end - 25), end);
  }

  protected siteMetric(l: LocationResult, key: string): Metric | undefined {
    return l.assessment.metrics.find(m => m.key === key);
  }

  protected flagged(m: Metric): boolean {
    return isFlagged(m.status);
  }

  protected dot(m: Metric): string {
    return isFlagged(m.status) ? polarityColor(m.polarity) : 'var(--ink)';
  }

  protected sample(m: Metric): string {
    if (m.kind === 'count') return `events this ${this.noun()}`.replace('events', m.sampleUnit);
    return `n = ${m.sampleSize} ${m.sampleUnit}${m.detail && m.key === 'odd_hours' ? ' · ' + m.detail : ''}`;
  }

  protected pct(part: number, whole: number): string {
    return percent(part, whole);
  }

  protected toggle(l: LocationResult): void {
    this.open.update(o => (o === l.location ? null : l.location));
  }

  protected pickWeek(endIso: string): void {
    this.params.set({ asOf: endIso });
  }
}
