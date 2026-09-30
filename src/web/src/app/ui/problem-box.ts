import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Problem } from '../api/models';

/** Renders an API ProblemDetails (or a network failure) as a readable message. */
@Component({
  selector: 'app-problem',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="problem" role="alert">
      <b>{{ title() }}</b>
      <span>{{ detail() }}</span>
    </div>
  `,
})
export class ProblemBox {
  readonly error = input<unknown>();

  private readonly problem = computed<Problem | null>(() => {
    const e = this.error();
    return e instanceof HttpErrorResponse && e.error && typeof e.error === 'object' ? (e.error as Problem) : null;
  });

  protected readonly title = computed(() => {
    const e = this.error();
    if (e instanceof HttpErrorResponse && e.status === 0) return 'Can’t reach the API';
    return this.problem()?.title ?? 'Something went wrong';
  });

  protected readonly detail = computed(() => {
    const e = this.error();
    if (e instanceof HttpErrorResponse && e.status === 0) return 'Start it with npm start from the repository root, then reload.';
    const p = this.problem();
    if (p?.errors) return Object.values(p.errors).flat().join(' ');
    return p?.detail ?? (e instanceof Error ? e.message : 'Try reloading the page.');
  });
}
