import { Injectable, signal } from '@angular/core';

/** What a message is telling the reader. */
export type ToastTone = 'success' | 'error';

/** One transient message. */
export interface Toast {
  readonly id: number;
  readonly text: string;
  readonly tone: ToastTone;
}

/** How long a message stays before it removes itself. */
const LIFETIME_MS = 5_000;

/**
 * Brief confirmations of things that already happened.
 *
 * The smallest mechanism the product actually needs: an agent was created, a
 * name was saved, an agent was activated. Each of those is a completed action
 * whose result is not otherwise visible, and saying nothing leaves the reader
 * wondering whether the button worked.
 *
 * <strong>Not an error-reporting channel.</strong> A failure that a reader must
 * act on belongs beside the thing that failed, where the context is — a toast
 * that disappears after five seconds is the wrong place for "your save was
 * rejected". The error tone exists for the few failures with nowhere better to
 * live, not as the default.
 *
 * No queueing policy, no positions, no actions, no severities beyond two. A
 * notification framework can be built when something needs one.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly items = signal<readonly Toast[]>([]);
  private nextId = 1;

  /** The messages currently showing. */
  readonly toasts = this.items.asReadonly();

  /** Confirms something that succeeded. */
  success(text: string): void {
    this.show(text, 'success');
  }

  /** Reports a failure that has nowhere more specific to appear. */
  error(text: string): void {
    this.show(text, 'error');
  }

  /** Removes a message early, when the reader dismisses it. */
  dismiss(id: number): void {
    this.items.update((current) => current.filter((toast) => toast.id !== id));
  }

  private show(text: string, tone: ToastTone): void {
    const id = this.nextId++;

    this.items.update((current) => [...current, { id, text, tone }]);

    // Self-removing. A message about something that already finished has no
    // reason to need dismissing, and a pile of them would cover the page.
    setTimeout(() => this.dismiss(id), LIFETIME_MS);
  }
}
