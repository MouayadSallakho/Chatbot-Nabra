import { DOCUMENT, DestroyRef, Injectable, inject, signal } from '@angular/core';
import { LOCAL_STORAGE, readStored, writeStored } from '@app/core/storage/local-storage';

/** Where the details panel's open or closed state is remembered, per browser. */
export const DETAILS_PANEL_STORAGE_KEY = 'omniai.inbox-details';

/** A reply being written, and the key it will be sent with. */
export interface ReplyDraft {
  readonly text: string;

  /**
   * The idempotency key for this text.
   *
   * Kept with the draft, not minted per click: a retry of a send whose response
   * was lost must carry the same key, so the server recognises it as the same
   * reply instead of storing it twice. A changed text is a different reply and
   * gets a new key.
   */
  readonly key: string;
}

/**
 * What the inbox's panes share while it is open (Phase 36.6).
 *
 * Provided by the conversations route, so it lives as long as the feature and
 * no longer. It holds only things that are the reader's own, in this tab:
 *
 * - **Drafts, per conversation, in memory.** Switching to another conversation
 *   and back keeps a half-written reply; nothing is sent anywhere or stored on
 *   disk, because a reply to a customer is not something to leave in a
 *   browser's storage for whoever uses the machine next.
 * - **Whether the details panel is open**, remembered in this browser only:
 *   a per-viewer convenience, never a server-held preference (D22).
 * - **A nudge after a change made here**, so the list re-reads without
 *   waiting for the realtime echo of the reader's own write.
 */
@Injectable()
export class InboxState {
  private readonly storage = inject(LOCAL_STORAGE);
  private readonly drafts = new Map<string, ReplyDraft>();
  private readonly view = inject(DOCUMENT).defaultView;

  /** Whether the details panel is open. Defaults to open on a wide screen. */
  readonly detailsOpen = signal(readStored(this.storage, DETAILS_PANEL_STORAGE_KEY) !== 'closed');

  /** Bumped after a write made from this inbox; the list re-reads when it changes. */
  readonly changes = signal(0);

  /**
   * Set when the reader opened a conversation from the list, so the
   * conversation moves focus to its heading once it is on screen.
   *
   * The shell's own heading focus stays out of the way while focus is inside
   * the page, which it is: on the row that was just followed.
   */
  readonly focusOnOpen = signal(false);

  constructor() {
    // Closing or reloading the tab is the one way a draft is lost; ask first,
    // as every editor in the application does.
    const listener = (event: BeforeUnloadEvent) => {
      if (this.hasDrafts()) {
        event.preventDefault();
      }
    };

    this.view?.addEventListener('beforeunload', listener);
    inject(DestroyRef).onDestroy(() => this.view?.removeEventListener('beforeunload', listener));
  }

  toggleDetails(): void {
    const open = !this.detailsOpen();

    this.detailsOpen.set(open);
    writeStored(this.storage, DETAILS_PANEL_STORAGE_KEY, open ? 'open' : 'closed');
  }

  draft(conversationId: string): ReplyDraft | null {
    return this.drafts.get(conversationId) ?? null;
  }

  /** Records the text being written; a new text gets a new key, the same text keeps its key. */
  keep(conversationId: string, text: string): ReplyDraft | null {
    if (text.trim() === '') {
      this.drafts.delete(conversationId);

      return null;
    }

    const current = this.drafts.get(conversationId);
    const next = current?.text === text ? current : { text, key: newKey() };

    this.drafts.set(conversationId, next);

    return next;
  }

  /** Forgets a draft once its reply is stored. */
  sent(conversationId: string): void {
    this.drafts.delete(conversationId);
  }

  hasDrafts(): boolean {
    return this.drafts.size > 0;
  }

  /** Tells the list that something changed here. */
  touched(): void {
    this.changes.update((n) => n + 1);
  }
}

function newKey(): string {
  return globalThis.crypto.randomUUID();
}
