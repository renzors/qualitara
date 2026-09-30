import { Injectable, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { Sensitivity, WindowDays } from './models';

const WINDOWS: WindowDays[] = [1, 7, 30];
const SENSITIVITIES: Sensitivity[] = ['relaxed', 'normal', 'strict'];

/**
 * The global controls (as-of, window, sensitivity) live in the URL query string,
 * so every view is shareable and the back button replays earlier states.
 */
@Injectable({ providedIn: 'root' })
export class ViewParams {
  private readonly router = inject(Router);
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap, { requireSync: true });

  /** Explicit as-of (ISO UTC), or null for "latest". */
  readonly asOf = computed(() => this.query().get('asOf'));

  readonly window = computed<WindowDays>(() => {
    const w = Number(this.query().get('window'));
    return WINDOWS.includes(w as WindowDays) ? (w as WindowDays) : 7;
  });

  readonly sensitivity = computed<Sensitivity>(() => {
    const s = this.query().get('sensitivity') as Sensitivity | null;
    return s && SENSITIVITIES.includes(s) ? s : 'normal';
  });

  /** Query string for API calls; defaults are omitted so the API applies its own. */
  readonly apiQuery = computed(() => {
    const p = new URLSearchParams();
    const asOf = this.asOf();
    if (asOf) p.set('asOf', asOf);
    if (this.window() !== 7) p.set('window', String(this.window()));
    if (this.sensitivity() !== 'normal') p.set('sensitivity', this.sensitivity());
    return p.toString();
  });

  set(patch: { asOf?: string | null; window?: WindowDays; sensitivity?: Sensitivity }): void {
    const queryParams: Record<string, string | null> = {};
    if ('asOf' in patch) queryParams['asOf'] = patch.asOf ?? null;
    if (patch.window !== undefined) queryParams['window'] = patch.window === 7 ? null : String(patch.window);
    if (patch.sensitivity !== undefined) queryParams['sensitivity'] = patch.sensitivity === 'normal' ? null : patch.sensitivity;
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge' });
  }
}

/** Appends a query string to a path, handling the empty case. */
export function withQuery(path: string, query: string): string {
  return query ? `${path}${path.includes('?') ? '&' : '?'}${query}` : path;
}
