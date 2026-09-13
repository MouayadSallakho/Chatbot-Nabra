import { Injectable } from '@angular/core';

/**
 * Prefix for what the widget remembers between reloads of the host page.
 *
 * Scoped by public key so two widgets served from this origin — a staging
 * endpoint and a live one, or two different businesses — never resume into each
 * other's conversation.
 */
const KEY_PREFIX = 'omniai.embed.session.';

/** A conversation the widget can pick up again, and the credential that opens it. */
export interface EmbedSession {
  readonly conversationId: string;
  readonly credential: string;
}

/**
 * What the embedded widget remembers so a page reload does not start again.
 *
 * <strong>`sessionStorage`, inside the OmniAI iframe, and nowhere else.</strong>
 * The hosted page can keep its credential in an `HttpOnly` cookie the script
 * cannot read; a widget on somebody else's website cannot, because third-party
 * cookies are blocked by default. The credential therefore has to live somewhere
 * JavaScript can reach, and the question is only which JavaScript.
 *
 * This storage belongs to the OmniAI origin. The embedding website is a different
 * origin and cannot read it, the browser partitions it per top-level site so two
 * different websites embedding the same widget do not share it, and it is cleared
 * when the browser session ends. The credential is never sent to the parent
 * through `postMessage`, never put in the iframe's URL, and never written to
 * `localStorage` — a value that outlived the tab would be a value still sitting
 * on a shared computer the next morning.
 *
 * <strong>Every access is guarded, and the fallback is memory.</strong> Storage
 * throws outright when a browser is configured to block site data. A widget that
 * crashed over a convenience value would be a worse failure than one that simply
 * cannot resume after a reload, so the in-memory copy keeps the current page
 * working and disappears with it.
 */
@Injectable()
export class EmbedSessionStore {
  /**
   * The fallback when storage is unavailable.
   *
   * Deliberately not a cache in front of storage: reading through to
   * `sessionStorage` every time is what makes a second widget instance, or a
   * storage cleared by the browser, immediately visible here.
   */
  private readonly memory = new Map<string, EmbedSession>();

  /** The conversation this widget was last talking in, if it remembers one. */
  read(publicKey: string): EmbedSession | null {
    try {
      const stored = window.sessionStorage.getItem(KEY_PREFIX + publicKey);

      return stored === null ? (this.memory.get(publicKey) ?? null) : this.parse(stored);
    } catch {
      return this.memory.get(publicKey) ?? null;
    }
  }

  /** Remembers a conversation, so the next reload of the host page resumes it. */
  write(publicKey: string, session: EmbedSession): void {
    this.memory.set(publicKey, session);

    try {
      window.sessionStorage.setItem(KEY_PREFIX + publicKey, JSON.stringify(session));
    } catch {
      // The in-memory copy above is the whole recovery. The widget works; only
      // resuming after a reload does not.
    }
  }

  /**
   * Forgets the remembered conversation, credential included.
   *
   * Called when the server no longer admits it — an expired session, a website
   * whose approval was withdrawn — and when the visitor starts a new one. Removing
   * the credential is the point: a value that cannot open anything must not stay
   * in storage waiting to be found.
   */
  clear(publicKey: string): void {
    this.memory.delete(publicKey);

    try {
      window.sessionStorage.removeItem(KEY_PREFIX + publicKey);
    } catch {
      // Same reasoning as write.
    }
  }

  /**
   * Reads a stored entry, treating anything malformed as absent.
   *
   * A half-written or hand-edited entry costs no request and no confusing 404.
   */
  private parse(stored: string): EmbedSession | null {
    try {
      const parsed: unknown = JSON.parse(stored);

      if (parsed === null || typeof parsed !== 'object') {
        return null;
      }

      const candidate = parsed as Partial<EmbedSession>;

      return typeof candidate.conversationId === 'string' &&
        candidate.conversationId.trim().length > 0 &&
        typeof candidate.credential === 'string' &&
        candidate.credential.trim().length > 0
        ? { conversationId: candidate.conversationId, credential: candidate.credential }
        : null;
    } catch {
      return null;
    }
  }
}
