import { DOCUMENT, DestroyRef, Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import {
  FORGOT_PASSWORD_PATH,
  LOGIN_PATH,
  REGISTER_PATH,
  RESET_PASSWORD_PATH,
} from '@app/core/auth/auth-guard';
import {
  ACCEPT_INVITATION_PATH,
  InvitationHandoff,
} from '@app/core/invitations/invitation-handoff';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { HOME_PATH } from '@app/layout/navigation';
import { AuthService } from './auth-service';

/** The pages somebody reads before they are signed in. */
export const SIGNED_OUT_PATHS: readonly string[] = [
  LOGIN_PATH,
  REGISTER_PATH,
  FORGOT_PASSWORD_PATH,
  RESET_PASSWORD_PATH,
  ACCEPT_INVITATION_PATH,
];

/** The least time between two session checks from one page. */
const CHECK_SPACING_MS = 60_000;

/**
 * Notices, on a signed-out page, that this browser has signed in elsewhere.
 *
 * Somebody signs in from another tab, comes back to the sign-in form, and would
 * otherwise sign in a second time. So when the window regains focus the page asks
 * again, and if there is a session goes where a fresh sign-in would go.
 *
 * <strong>Never on a timer, and at most once a minute.</strong> The session check
 * is limited per network, and everybody behind one address shares that limit. A
 * 429 stops the checks until `Retry-After` has passed; a 503 or no answer leaves
 * the page exactly as it was.
 *
 * Signed-in pages do not ask: their ordinary reads meet a 401 when the session
 * ends, and the session interceptor takes it from there.
 */
@Injectable({ providedIn: 'root' })
export class SessionWatch {
  private readonly document = inject(DOCUMENT);
  private readonly auth = inject(AuthService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly invitations = inject(InvitationHandoff);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  private started = false;
  private asking = false;
  private notBefore = 0;
  private redirectHeld = false;

  /** Begins watching. Startup's own check counts as the first one. */
  start(): void {
    if (this.started) {
      return;
    }

    this.started = true;
    this.waitAfterCheck(this.auth.startupRetryAfterSeconds);

    const check = (): void => void this.check();
    const whenVisible = (): void => {
      if (this.document.visibilityState === 'visible') {
        check();
      }
    };
    const view = this.document.defaultView;

    view?.addEventListener('focus', check);
    this.document.addEventListener('visibilitychange', whenVisible);

    this.destroyRef.onDestroy(() => {
      view?.removeEventListener('focus', check);
      this.document.removeEventListener('visibilitychange', whenVisible);
    });
  }

  /**
   * While true, finding a session does not move the page. For a page whose
   * reader would lose something by being moved: a reset link lives only in memory.
   */
  holdRedirect(held: boolean): void {
    this.redirectHeld = held;
  }

  private async check(): Promise<void> {
    if (
      this.asking ||
      Date.now() < this.notBefore ||
      this.auth.status() !== 'anonymous' ||
      !this.onSignedOutPage()
    ) {
      return;
    }

    this.asking = true;

    const answer = await this.auth.checkSession();

    this.asking = false;
    this.waitAfterCheck(answer.outcome === 'unavailable' ? answer.retryAfterSeconds : undefined);

    if (answer.outcome === 'answered' && answer.user && !this.redirectHeld) {
      this.auth.adoptAnswer(answer.user);
      await this.goWhereASignInGoes();
    }
  }

  private waitAfterCheck(retryAfterSeconds: number | undefined): void {
    this.notBefore = Date.now() + Math.max(CHECK_SPACING_MS, (retryAfterSeconds ?? 0) * 1000);
  }

  private onSignedOutPage(): boolean {
    const first = this.document.location.pathname.split('/').filter(Boolean)[0] ?? '';

    return SIGNED_OUT_PATHS.includes(first);
  }

  /** The same destinations as the sign-in screen. */
  private async goWhereASignInGoes(): Promise<void> {
    await this.workspace.load();

    if (this.invitations.pending()) {
      await this.router.navigate(['/', ACCEPT_INVITATION_PATH]);

      return;
    }

    await this.router.navigate(['/', this.workspace.isReady() ? HOME_PATH : WORKSPACE_SETUP_PATH]);
  }
}
