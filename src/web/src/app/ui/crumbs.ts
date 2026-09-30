import { DestroyRef, Injectable, Signal, computed, inject, signal } from '@angular/core';

/** Lets a page name itself in the top bar's breadcrumb (e.g. the open account). */
@Injectable({ providedIn: 'root' })
export class Crumbs {
  private readonly source = signal<Signal<string | null> | null>(null);
  readonly current = computed(() => this.source()?.() ?? null);

  /** Call from a page constructor; cleared automatically when the page is destroyed. */
  bind(title: Signal<string | null>): void {
    this.source.set(title);
    inject(DestroyRef).onDestroy(() => {
      if (this.source() === title) this.source.set(null);
    });
  }
}
