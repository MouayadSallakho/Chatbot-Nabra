import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { SESSION_ENDED } from '@app/core/auth/session-expiry-interceptor';
import { FORGOT_PASSWORD_PATH, REGISTER_PATH } from '@app/core/auth/auth-guard';
import { APP_LANGUAGE } from '@app/core/i18n/language';
import { THROTTLED_CODE, throttledMessage } from '@app/core/problems/core-problems';
import { refusedFields } from '@app/core/problems/field-problems';
import {
  ACCEPT_INVITATION_PATH,
  InvitationHandoff,
} from '@app/core/invitations/invitation-handoff';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { HOME_PATH } from '@app/layout/navigation';
import { Icon } from '@app/shared/icon/icon';
import { EntryFrame } from '@app/layout/entry-frame/entry-frame';

/** The fields of this form, by the names the server uses for them too. */
const FIELDS = ['email', 'password'] as const;
type Field = (typeof FIELDS)[number];

/** The least time between two announcements of how long is left. */
const ANNOUNCE_EVERY_MS = 60_000;

/**
 *  * The sign-in screen.
 *
 * <strong>The password is never held.</strong> It lives in a form control until
 * submit, goes into the request body, and the control is cleared on failure.
 * Nothing writes it to a signal, a field, `localStorage`, `sessionStorage` or a
 * log, and nothing reads it back.
 *
 * <strong>One sentence for every wrong answer.</strong> The backend answers a
 * wrong password, an unknown address and a locked account with one code, so that
 * this form cannot be used to discover which addresses are registered. The words
 * for that code say nothing more, in either language.
 *
 * <strong>Being refused for too many attempts names the wait.</strong> The button
 * stays disabled until it has passed, counted on this device from the moment of
 * the answer. The time left is announced politely and at most once a minute: a
 * screen reader reading out every second would make the page unusable.
 */
@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EntryFrame, ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './login.html',
  styleUrl: '../auth-page.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly invitations = inject(InvitationHandoff);
  private readonly router = inject(Router);
  private readonly language = inject(APP_LANGUAGE);

  /**
   * Whether the reader arrived here because their session ended mid-task, as
   * opposed to opening the sign-in screen themselves.
   */
  protected readonly sessionEnded =
    inject(ActivatedRoute).snapshot.queryParamMap.get('reason') === SESSION_ENDED;

  protected readonly registerPath = REGISTER_PATH;
  protected readonly forgotPasswordPath = FORGOT_PASSWORD_PATH;

  /** Whether this sign-in is a step inside accepting an invitation. */
  protected readonly invitationPending = this.invitations.pending;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly passwordVisible = signal(false);

  /** Fields a refusal named, until the reader edits them. */
  protected readonly refused = signal<ReadonlySet<Field>>(new Set());

  /** Whether the button is held back because too many attempts were made. */
  protected readonly waiting = signal(false);

  /** The wait has just ended, and nothing has been tried since. */
  protected readonly waitOver = signal(false);

  /** What the always-present polite live region is saying. */
  protected readonly waitAnnouncement = signal('');

  protected readonly waitOverText = $localize`:@@signIn.waitOver:You can try signing in again now.`;

  private waitEndsAt = 0;
  private waitTimer: ReturnType<typeof setTimeout> | undefined;

  private readonly submitAttempted = signal(false);

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required],
    }),
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.waitTimer));
  }

  /**
   * Whether a field's error belongs on screen yet.
   *
   * Only after the reader has left the field or tried to submit. Marking a
   * required field invalid while somebody is typing its first character is
   * accurate and reads as nagging.
   */
  protected showError(field: Field): boolean {
    const control = this.form.controls[field];

    return (
      (control.invalid && (control.touched || this.submitAttempted())) || this.refused().has(field)
    );
  }

  /** A refusal is about the value it judged, so editing the field clears it. */
  protected fieldEdited(field: Field): void {
    if (this.refused().has(field)) {
      this.refused.update((fields) => new Set([...fields].filter((name) => name !== field)));
    }
  }

  /**
   * Reveals or hides the password.
   *
   * A control the reader operates deliberately, not a timed reveal. It helps
   * with a long passphrase on a laptop in private, and it never turns itself on.
   */
  protected togglePassword(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  protected async submit(): Promise<void> {
    this.submitAttempted.set(true);

    // Guards against a double submit as well as an invalid one: without it a
    // fast second click sends a second sign-in and can produce two navigations.
    if (this.form.invalid || this.submitting() || this.waiting()) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);
    this.refused.set(new Set());
    this.waitOver.set(false);
    this.waitAnnouncement.set('');

    try {
      await this.auth.login({
        email: this.form.controls.email.value.trim(),
        password: this.form.controls.password.value,
      });

      // The account's workspaces are loaded before navigating, so the reader
      // arrives at a screen that already knows whether they have one. Navigating
      // first would show the admin shell and then bounce it to the workspace
      // screen, which reads as a broken redirect.
      await this.workspace.load();

      // An invitation waiting in memory means this sign-in was a step inside
      // accepting one, so the reader goes back to finish it rather than to a
      // workspace they may not have yet. Checked here rather than through a
      // return-url parameter: the token must not travel in a URL, and inventing
      // a general redirect mechanism for one case would be more machinery than
      // the case needs.
      if (this.invitations.pending()) {
        await this.router.navigate(['/', ACCEPT_INVITATION_PATH]);

        return;
      }

      await this.router.navigate([
        '/',
        this.workspace.isReady() ? HOME_PATH : WORKSPACE_SETUP_PATH,
      ]);
    } catch (error) {
      const failure = error as ApiError;

      this.failure.set(failure);
      this.refused.set(refusedFields(failure, FIELDS));
      this.beginWait(failure);

      // The password is wrong, or the account is locked, or it does not exist.
      // Whichever it was, this copy is no longer wanted: leaving it in the field
      // invites the same failed attempt again and leaves it on screen if the
      // reader walks away.
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }

  /** Holds the button back until `Retry-After` has passed. */
  private beginWait(failure: ApiError): void {
    if (failure.code !== THROTTLED_CODE || !failure.retryAfterSeconds) {
      return;
    }

    this.waitEndsAt = Date.now() + failure.retryAfterSeconds * 1000;
    this.waiting.set(true);
    this.waitAnnouncement.set(failure.message);
    this.scheduleNextUpdate(failure.retryAfterSeconds * 1000);
  }

  private scheduleNextUpdate(remainingMs: number): void {
    clearTimeout(this.waitTimer);
    this.waitTimer = setTimeout(() => this.updateWait(), Math.min(ANNOUNCE_EVERY_MS, remainingMs));
  }

  private updateWait(): void {
    const remainingMs = this.waitEndsAt - Date.now();

    if (remainingMs <= 0) {
      this.waiting.set(false);
      this.failure.set(null);
      this.waitOver.set(true);
      this.waitAnnouncement.set(this.waitOverText);

      return;
    }

    const message = throttledMessage(Math.ceil(remainingMs / 1000), this.language);

    this.failure.update((failure) => failure && { ...failure, message });
    this.waitAnnouncement.set(message);
    this.scheduleNextUpdate(remainingMs);
  }
}
