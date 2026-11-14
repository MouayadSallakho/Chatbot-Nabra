import { EnvironmentInjector, Injectable, computed, inject, signal } from '@angular/core';
import type { InboxSignals } from '@app/core/sounds/inbox-signals';

/**
 * The counts the navigation shows beside an area (Phase 39.3): the inbox's
 * needs-a-person count.
 *
 * <strong>No read of its own.</strong> The count is the one `InboxSignals`
 * already keeps for the inbox and its sounds: one coalesced read per browser and
 * workspace, triggered by the hub, shared between tabs (38.2b). Showing it in
 * the side bar or the bottom bar only tells that reader the count is on screen.
 *
 * `InboxSignals` stays a lazy chunk: it is fetched the first time a count is
 * shown, never on the startup path, and only for a member who may read the
 * inbox (the caller asks only then). `InboxSignals` checks the permission again
 * before it reads anything.
 */
@Injectable({ providedIn: 'root' })
export class NavCounts {
  private readonly injector = inject(EnvironmentInjector);
  private readonly reader = signal<InboxSignals | null>(null);
  private loading: Promise<InboxSignals | null> | null = null;

  /** How many conversations need a person; null until it is known. */
  readonly needsPerson = computed(() => this.reader()?.count() ?? null);

  /**
   * The count is on screen. Returns what to call when it no longer is, which
   * is safe to call before the reader has even loaded.
   */
  show(): () => void {
    let release: (() => void) | null = null;
    let released = false;

    void this.load().then((reader) => {
      if (reader && !released) {
        release = reader.showCount();
      }
    });

    return () => {
      released = true;
      release?.();
      release = null;
    };
  }

  private load(): Promise<InboxSignals | null> {
    this.loading ??= import('@app/core/sounds/inbox-signals')
      .then(({ InboxSignals }) => {
        const reader = this.injector.get(InboxSignals);

        this.reader.set(reader);

        return reader;
      })
      .catch(() => {
        // A chunk that did not arrive: no count, and the inbox itself still
        // says it. The next caller tries again.
        this.loading = null;

        return null;
      });

    return this.loading;
  }
}
