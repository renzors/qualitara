import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink, RouterOutlet } from '@angular/router';
import { Meta, Sensitivity, WindowDays } from './api/models';
import { ViewParams } from './api/view-params';
import { Crumbs } from './ui/crumbs';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink],
  template: `
    <div class="wrap">
      <header class="top">
        <a class="brand" routerLink="/accounts" queryParamsHandling="preserve">Relay <span>Scorecard</span></a>
        <nav class="crumbs" aria-label="Breadcrumb">
          @if (crumbs.current(); as name) {
            <a routerLink="/accounts" queryParamsHandling="preserve">All accounts</a> / {{ name }}
          } @else {
            All accounts
          }
        </nav>
        <div class="ctlrow">
          <label for="asof">As of
            <input class="date mono" id="asof" type="date" [min]="minDate()" [max]="maxDate()" [value]="asOfDate()"
              (change)="pickDate($any($event.target).value)" aria-describedby="asof-time">
            <span class="mono small" id="asof-time">{{ asOfTime() }}</span>
          </label>
          <button type="button" class="linkbtn" (click)="params.set({ asOf: null })" [disabled]="!params.asOf()">Latest</button>
          <div class="seg" role="group" aria-label="Window">
            @for (w of windows; track w.value) {
              <button type="button" [attr.aria-pressed]="params.window() === w.value" (click)="params.set({ window: w.value })">{{ w.label }}</button>
            }
          </div>
          <div class="seg" role="group" aria-label="Sensitivity">
            @for (s of sensitivities; track s.value) {
              <button type="button" [attr.aria-pressed]="params.sensitivity() === s.value" (click)="params.set({ sensitivity: s.value })">{{ s.label }}</button>
            }
          </div>
        </div>
      </header>
      <main class="page">
        <router-outlet />
      </main>
    </div>
  `,
})
export class App {
  protected readonly params = inject(ViewParams);
  protected readonly crumbs = inject(Crumbs);
  private readonly meta = httpResource<Meta>(() => '/api/meta');

  protected readonly windows: { value: WindowDays; label: string }[] = [
    { value: 1, label: '1d' }, { value: 7, label: '7d' }, { value: 30, label: '30d' },
  ];
  protected readonly sensitivities: { value: Sensitivity; label: string }[] = [
    { value: 'relaxed', label: 'Relaxed' }, { value: 'normal', label: 'Normal' }, { value: 'strict', label: 'Strict' },
  ];

  private readonly m = computed(() => (this.meta.hasValue() ? this.meta.value() : undefined));
  private readonly effectiveAsOf = computed(() => this.params.asOf() ?? this.m()?.defaultAsOf ?? null);

  protected readonly minDate = computed(() => this.m()?.minAsOf.slice(0, 10) ?? null);
  protected readonly maxDate = computed(() => this.m()?.maxAsOf.slice(0, 10) ?? null);
  protected readonly asOfDate = computed(() => this.effectiveAsOf()?.slice(0, 10) ?? '');
  protected readonly asOfTime = computed(() => {
    const iso = this.effectiveAsOf();
    return iso ? `${iso.slice(11, 16)} UTC${this.params.asOf() ? '' : ' · latest'}` : '';
  });

  /** A picked date means the end of that day in UTC (decision D4). */
  protected pickDate(value: string): void {
    if (!value) {
      this.params.set({ asOf: null });
      return;
    }
    const max = this.m()?.maxAsOf;
    const iso = `${value}T23:59:59Z`;
    this.params.set({ asOf: max && iso > max ? max : iso });
  }
}
