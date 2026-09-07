import { Injectable } from '@angular/core';

/**
 * Prefix for the one thing this page remembers between reloads.
 *
 * Scoped by public key so two chats on the same origin — a staging endpoint and
 * a live one, or two different businesses hosted here — never resume into each
 * other's conversation.
 */
const KEY_PREFIX = 'omniai.webchat.conversation.';

/**
 * What the hosted chat page remembers so a reload does not start again.
 *
 * <strong>It stores a conversation id, and nothing else, ever.</strong> A
 * conversation id is not a credential: it appears in an administrator's list, it
 * is returned to the page that created it, and knowing one grants nothing. The
 * secret that actually opens the conversation stays in an `HttpOnly` cookie this
 * script cannot read — so a reload works because the *browser* still holds the
 * credential, not because this class kept a copy of it.
 *
 * <strong>`sessionStorage`, not `localStorage`.</strong> The credential cookie is
 * session-scoped, so remembering the conversation for longer than the browser
 * session would leave a resume id whose cookie is already gone — a page that
 * asks for a transcript it can no longer read. Matching the two lifetimes means
 * the stale case is rare rather than routine, and it also means a stranger's
 * conversation is not offered up on a shared computer the next morning.
 *
 * <strong>Every access is guarded.</strong> Storage throws outright in some
 * contexts — a browser configured to block site data, a sandboxed frame — and a
 * chat page that crashed because it could not write a convenience value would be
 * a worse failure than one that simply starts a new conversation.
 */
@Injectable()
export class HostedChatSessionStore {
  /** The conversation this browser was last talking in, if it remembers one. */
  read(publicKey: string): string | null {
    try {
      const stored = window.sessionStorage.getItem(KEY_PREFIX + publicKey);

      // A blank or junk value is treated as absent rather than sent to the
      // server, so a corrupted entry costs no request and no confusing 404.
      return stored !== null && stored.trim().length > 0 ? stored : null;
    } catch {
      return null;
    }
  }

  /** Remembers a conversation, so the next reload resumes it. */
  write(publicKey: string, conversationId: string): void {
    try {
      window.sessionStorage.setItem(KEY_PREFIX + publicKey, conversationId);
    } catch {
      // Nothing to recover. The chat works without this; only resuming after a
      // reload does not.
    }
  }

  /**
   * Forgets the remembered conversation.
   *
   * Called when the server no longer admits it — an expired session, a
   * conversation that has gone — and when the visitor deliberately starts a new
   * one. It does not and cannot remove the credential cookie, which is
   * `HttpOnly`; that expires with the browser session.
   */
  clear(publicKey: string): void {
    try {
      window.sessionStorage.removeItem(KEY_PREFIX + publicKey);
    } catch {
      // Same reasoning as write.
    }
  }
}
