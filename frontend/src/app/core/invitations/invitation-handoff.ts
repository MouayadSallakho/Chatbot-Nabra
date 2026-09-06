import { Injectable, signal } from '@angular/core';

/**
 * Where an invitation link lands.
 *
 * Exported beside the service that holds the token because the two are always
 * needed together: anything that resumes an interrupted acceptance has to know
 * both that one is pending and where to send the reader.
 */
export const ACCEPT_INVITATION_PATH = 'accept-invitation';

/**
 * The invitation token, held in memory for as long as it takes to use it.
 *
 * <strong>Memory only, and that is the whole design.</strong> An invitation
 * token is a bearer credential: anybody holding it can present it. Writing it to
 * `localStorage` would leave it on disk indefinitely, readable by every later
 * script on the origin and surviving the browser being closed; `sessionStorage`
 * is the same exposure with a shorter fuse. Neither is worth the convenience of
 * surviving a refresh, because the cost of not surviving one is that somebody
 * reopens the link they were sent.
 *
 * <strong>Why it exists at all.</strong> An invitee who is not signed in has to
 * authenticate before they can accept, and that means navigating away from the
 * acceptance page and back. The token cannot ride along in the URL — the whole
 * point of the fragment handling is to get it out of the address bar — so
 * something has to hold it across those navigations. A root-provided service
 * lives exactly as long as the tab, which is the correct lifetime.
 *
 * <strong>It is not a credential.</strong> Nothing here goes into a cookie, a
 * header, or the authentication state. It is an argument to one API call, and it
 * is discarded the moment that call resolves either way.
 */
@Injectable({ providedIn: 'root' })
export class InvitationHandoff {
  private readonly token = signal<string | null>(null);

  /** Whether a token is waiting to be used. */
  readonly pending = signal(false);

  /** Remembers a token captured from an invitation link. */
  hold(token: string): void {
    this.token.set(token);
    this.pending.set(true);
  }

  /** The token, if one is held. */
  peek(): string | null {
    return this.token();
  }

  /**
   * Forgets it.
   *
   * Called once acceptance has succeeded or failed for a reason retrying cannot
   * fix. Keeping a spent token would mean a later visit to the acceptance page
   * silently retried something already concluded.
   */
  clear(): void {
    this.token.set(null);
    this.pending.set(false);
  }
}
