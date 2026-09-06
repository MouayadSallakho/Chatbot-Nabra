import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  Event as RouterEvent,
  NavigationEnd,
  NavigationSkipped,
  Router,
  RouterLink,
} from '@angular/router';
import { filter, firstValueFrom, fromEvent } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { LOGIN_PATH, REGISTER_PATH } from '@app/core/auth/auth-guard';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { HOME_PATH } from '@app/layout/navigation';
import { AcceptedInvitation, TeamApi } from '@app/features/team/team-api';

/** What this screen is currently doing. */
type AcceptState = 'reading' | 'needsAccount' | 'accepting' | 'accepted' | 'failed';

/**
 * The page an invitation link opens.
 *
 * <strong>The token arrives in the fragment, and leaves the address bar
 * immediately.</strong> A fragment is never sent to the server, never appears in
 * a request line, and is not forwarded in a `Referer` header — which a query
 * string is, and does. That gets the secret to this page safely; what happens
 * next is this component's job. It is read once, moved into memory, and the
 * fragment is stripped with `history.replaceState` so the value is not sitting
 * in a visible URL to be screenshotted, read over a shoulder, or copied out of
 * the address bar by somebody who meant to copy the page.
 *
 * <strong>Stripping it costs a refresh, and that is the right trade.</strong>
 * Reloading this page after capture loses the token and the reader has to reopen
 * their link. The alternative — keeping a bearer credential in the URL, or
 * writing it to storage so a refresh survives — trades a permanent exposure for
 * a moment's convenience.
 *
 * <strong>Anonymous visitors are expected, not an error.</strong> Most people
 * opening an invitation do not have an account yet. They are offered sign-in and
 * registration, and the token waits in memory for as long as the tab lives, so
 * acceptance continues by itself when they come back authenticated.
 *
 * <strong>Outside the workspace shell.</strong> The shell's guards require a
 * workspace, and somebody accepting their first invitation has none — sending
 * them through it would bounce them to the workspace-setup screen and lose the
 * invitation entirely.
 */
@Component({
  selector: 'app-accept-invitation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  templateUrl: './accept-invitation.html',
  styleUrl: '../../auth/auth-page.scss',
})
export class AcceptInvitation implements OnInit {
  private readonly api = inject(TeamApi);
  private readonly auth = inject(AuthService);
  private readonly handoff = inject(InvitationHandoff);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly state = signal<AcceptState>('reading');
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly workspaceName = signal<string | null>(null);

  protected readonly loginPath = `/${LOGIN_PATH}`;
  protected readonly registerPath = `/${REGISTER_PATH}`;
  protected readonly homePath = `/${HOME_PATH}`;

  /** Whether a token was found at all. */
  protected readonly hasToken = computed(() => this.handoff.pending());

  ngOnInit(): void {
    this.begin();

    // Another invitation can arrive in a tab that already handled one --
    // somebody forwards a second link, or the same person is invited to two
    // workspaces. Opening it changes only the fragment, which is a same-document
    // navigation: this component is never destroyed and `ngOnInit` never runs
    // again. Reading the token once would leave the previous outcome on screen
    // and ignore the new link entirely, telling somebody they had joined a
    // workspace they had not.
    //
    // <strong>The browser's event, not the router's fragment.</strong> `capture`
    // rewrites the URL with `history.replaceState`, which the router does not
    // observe, so its remembered fragment still holds the spent token -- and
    // reopening the *same* link would then be a no-op, because the value it
    // compares against never changed. `hashchange` has no such memory: it fires
    // whenever the address bar's fragment actually differs, which is exactly the
    // condition this needs. `replaceState` does not raise it, so stripping the
    // token cannot re-enter this.
    fromEvent(window, 'hashchange')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (window.location.hash.includes('token=')) {
          this.begin();
        }
      });

    // <strong>The router is the other writer of this URL, and it writes last.</strong>
    // A fragment change on a live page starts a navigation; when that navigation
    // finishes, the router writes the URL it parsed -- token and all -- back into
    // the address bar, undoing the strip that has already happened. Racing it
    // would mean depending on which write lands second, so this is a standing
    // correction rather than a one-off: whenever a navigation settles, a token
    // must not be in the address bar. Removing it with `replaceState` starts no
    // navigation, so this cannot re-enter itself.
    this.router.events
      .pipe(
        filter(
          (event: RouterEvent) =>
            event instanceof NavigationEnd || event instanceof NavigationSkipped,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        if (window.location.hash.includes('token=')) {
          this.strip();
        }
      });
  }

  /** Takes the token from the URL and acts on it. */
  private begin(): void {
    this.capture();

    void this.proceed();
  }

  /**
   * Takes the token out of the URL.
   *
   * <strong>Read from `location.hash` rather than from the router.</strong> The
   * router's fragment is a snapshot of what it parsed, and this needs the live
   * value plus the ability to rewrite it in the same breath.
   */
  private capture(): void {
    const hash = window.location.hash;

    if (!hash.includes('token=')) {
      return;
    }

    const token = new URLSearchParams(hash.replace(/^#/, '')).get('token');

    if (token) {
      this.handoff.hold(token);
    }

    this.strip();
  }

  /**
   * Takes the fragment out of the address bar.
   *
   * `replaceState` rather than a navigation, so no new entry appears: pressing
   * Back must not return to a URL that still contains the secret. It is also
   * synchronous, which matters -- the token is gone from the bar in the same
   * tick it was read.
   */
  private strip(): void {
    window.history.replaceState(
      window.history.state,
      '',
      window.location.pathname + window.location.search,
    );
  }

  /** Accepts if we can, or explains what is needed first. */
  protected async proceed(): Promise<void> {
    if (!this.handoff.peek()) {
      this.state.set('failed');
      this.failure.set({
        kind: 'unknown',
        status: 0,
        message:
          'This invitation link is missing its code. Open the link you were sent again, or ' +
          'ask for a new invitation.',
      });

      return;
    }

    if (!this.auth.isAuthenticated()) {
      this.state.set('needsAccount');

      return;
    }

    await this.accept();
  }

  private async accept(): Promise<void> {
    const token = this.handoff.peek();

    if (!token) {
      return;
    }

    this.state.set('accepting');
    this.failure.set(null);

    try {
      const accepted: AcceptedInvitation = await firstValueFrom(this.api.accept(token));

      // Spent, whatever happens next. Holding it would mean a later visit
      // silently retried something already concluded.
      this.handoff.clear();

      // The workspace list is re-read and the new one selected before anything
      // navigates, so the shell opens knowing where it is. Relying on state from
      // before the acceptance would show a reader an admin with no workspace in
      // it, or the wrong one.
      await this.workspace.load();
      await this.workspace.select(accepted.tenantId);

      this.workspaceName.set(accepted.name);
      this.state.set('accepted');
    } catch (error) {
      const failure = toApiError(error);

      this.failure.set(this.explain(failure));
      this.state.set('failed');

      // A refused invitation is refused for good: used, withdrawn, expired, or
      // written to somebody else. Keeping the token would offer a retry that
      // cannot succeed.
      this.handoff.clear();
    }
  }

  /**
   * Says what to do next, for the one refusal whose shared copy misleads.
   *
   * A `403` here is not the usual "your roles do not include this": the reader
   * is not being refused inside a workspace they belong to, they are being told
   * this link was written to somebody else's address. The shared copy would send
   * them to an administrator, who cannot help — the remedy is theirs, and it is
   * to come back as the invited account.
   *
   * The address is still never named. Repeating it would turn a forwarded link
   * into a way to learn a colleague's email, which is exactly what refusing the
   * acceptance is protecting.
   */
  private explain(failure: ApiError): ApiError {
    if (failure.kind !== 'forbidden') {
      return failure;
    }

    return {
      ...failure,
      message:
        'This invitation was sent to a different account. Sign out and open the link again ' +
        'as the address it was sent to.',
    };
  }

  /** Into the workspace they just joined. */
  protected async open(): Promise<void> {
    await this.router.navigate(['/', HOME_PATH]);
  }
}
