import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import {
  Event as RouterEvent,
  NavigationEnd,
  NavigationSkipped,
  Router,
  RouterLink,
} from '@angular/router';
import { filter, fromEvent } from 'rxjs';
import { toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { FORGOT_PASSWORD_PATH, LOGIN_PATH } from '@app/core/auth/auth-guard';
import { PASSWORD_MIN_LENGTH } from '@app/core/auth/password-policy';
import { SessionWatch } from '@app/core/auth/session-watch';
import { passwordProblemLines } from '@app/core/problems/core-problems';
import { refusedFields } from '@app/core/problems/field-problems';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { Icon } from '@app/shared/icon/icon';
import { EntryFrame } from '@app/layout/entry-frame/entry-frame';
import { describeResetFailure } from '../password-reset-failure';

/**
 * What this screen is showing.
 *
 * `invalid` is a link that arrived without its parts; `expired` is one the
 * server refused. They are told apart because the first is usually a link that
 * was cut short when it was copied, and opening it again from the email fixes
 * it, while the second needs a new link.
 */
type ResetState = 'choosing' | 'invalid' | 'expired' | 'done';

/** The two parts of a reset link, held in memory until they are used. */
interface ResetLink {
  readonly userId: string;
  readonly token: string;
}

/** Bad, expired or already used link, or an unknown account: one code for all of them. */
const RESET_INVALID_CODE = 'auth.reset.invalid';

/** Whether a fragment carries any part of a reset link. */
function carriesLink(hash: string): boolean {
  return /(^|[#&])(uid|token)=/.test(hash);
}

/** Both fields hold the same password. */
function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const password = group.get('password')?.value;
  const confirmation = group.get('confirmation')?.value;

  return confirmation && password !== confirmation ? { mismatch: true } : null;
}

/**
 * The page a password-reset link opens.
 *
 * <strong>The link's parts arrive in the fragment and leave the address bar
 * immediately</strong>, exactly as an invitation's token does
 * (`AcceptInvitation`): a fragment is never sent to the server or forwarded in
 * a `Referer` header, and once read it is stripped with
 * `history.replaceState`, so it is not sitting in a visible URL to be
 * screenshotted, copied out, or kept in the history. The token is then held in
 * a private field of this component and nowhere else -- not a signal the
 * template reads, not storage, not a log -- and forgotten as soon as the server
 * has answered for it.
 *
 * <strong>Stripping it costs a refresh, and that is the right trade.</strong>
 * Reloading after capture loses the link and the reader opens it again from
 * their email. The language switch, which reloads, is withheld for as long as
 * a link is held.
 *
 * <strong>Nothing here signs anybody in.</strong> The server ends every session
 * the account had; this browser reached the page anonymously and signs in with
 * the new password like anybody else.
 */
@Component({
  selector: 'app-reset-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EntryFrame, ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './reset-password.html',
  styleUrl: '../auth-page.scss',
})
export class ResetPassword implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly invitations = inject(InvitationHandoff);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly sessionWatch = inject(SessionWatch);

  /** The link being used. Never rendered, stored or logged. */
  private link: ResetLink | null = null;

  protected readonly loginPath = `/${LOGIN_PATH}`;
  protected readonly forgotPasswordPath = `/${FORGOT_PASSWORD_PATH}`;

  protected readonly passwordHint = $localize`:@@resetPassword.password.hint:At least ${PASSWORD_MIN_LENGTH}:count: characters. A memorable phrase works well.`;
  protected readonly passwordError = $localize`:@@resetPassword.password.error:Use at least ${PASSWORD_MIN_LENGTH}:count: characters.`;

  protected readonly state = signal<ResetState>('choosing');

  /**
   * Whether a link is held. The reload a language switch makes would lose it,
   * and so would one an invitation waiting in memory.
   */
  protected readonly holding = signal(false);
  protected readonly invitationPending = this.invitations.pending;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly passwordVisible = signal(false);

  /** What the server said is wrong with the password, translated, one line per rule. */
  protected readonly passwordProblems = signal<readonly string[]>([]);

  private readonly submitAttempted = signal(false);

  protected readonly form = new FormGroup(
    {
      password: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH)],
      }),
      confirmation: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required],
      }),
    },
    { validators: passwordsMatch },
  );

  constructor() {
    // Somebody signing in from another tab must not cost this page its link,
    // which lives only in memory.
    effect(() => this.sessionWatch.holdRedirect(this.holding()));
    this.destroyRef.onDestroy(() => this.sessionWatch.holdRedirect(false));
  }

  ngOnInit(): void {
    this.begin();

    // Another reset link opened in this tab changes only the fragment: a
    // same-document navigation that never runs `ngOnInit` again. The browser's
    // `hashchange` notices it; `replaceState` does not raise it, so stripping
    // cannot re-enter this. (The reasoning is AcceptInvitation's, in full.)
    fromEvent(window, 'hashchange')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (carriesLink(window.location.hash)) {
          this.begin();
        }
      });

    // The router writes the URL it parsed back into the address bar when a
    // navigation settles, fragment and all, undoing a strip that already
    // happened. Whenever one settles, no part of a link may be in the bar.
    this.router.events
      .pipe(
        filter(
          (event: RouterEvent) =>
            event instanceof NavigationEnd || event instanceof NavigationSkipped,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        if (carriesLink(window.location.hash)) {
          this.strip();
        }
      });
  }

  /** Takes the link from the URL and shows the form, or says it cannot be used. */
  private begin(): void {
    this.link = this.capture();
    this.holding.set(this.link !== null);

    this.form.reset();
    this.submitAttempted.set(false);
    this.failure.set(null);
    this.passwordProblems.set([]);
    this.state.set(this.link ? 'choosing' : 'invalid');
  }

  /**
   * Reads the link's two parts from the fragment, then strips it.
   *
   * Read from `location.hash` rather than from the router, which holds only a
   * snapshot of what it parsed. Stripped whether or not both parts were there:
   * half a link is still half a secret.
   */
  private capture(): ResetLink | null {
    const hash = window.location.hash;

    if (!carriesLink(hash)) {
      return null;
    }

    const parts = new URLSearchParams(hash.replace(/^#/, ''));
    const userId = parts.get('uid')?.trim() ?? '';
    const token = parts.get('token')?.trim() ?? '';

    this.strip();

    return userId && token ? { userId, token } : null;
  }

  /**
   * Takes the fragment out of the address bar.
   *
   * `replaceState` rather than a navigation, so no history entry is added that
   * Back would return to, and synchronously, so the link is gone from the bar in
   * the same tick it was read.
   */
  private strip(): void {
    window.history.replaceState(
      window.history.state,
      '',
      window.location.pathname + window.location.search,
    );
  }

  /** Forgets the link. Spent or refused, it is of no further use. */
  private forget(): void {
    this.link = null;
    this.holding.set(false);
  }

  protected showPasswordError(): boolean {
    const control = this.form.controls.password;

    return control.invalid && (control.touched || this.submitAttempted());
  }

  /** What is wrong with the confirmation, once it belongs on screen. */
  protected confirmationError(): 'missing' | 'mismatch' | null {
    const control = this.form.controls.confirmation;

    if (!control.touched && !this.submitAttempted()) {
      return null;
    }

    if (control.invalid) {
      return 'missing';
    }

    return this.form.hasError('mismatch') ? 'mismatch' : null;
  }

  protected togglePassword(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  /** The server's verdict on the password belongs to the password it judged. */
  protected passwordEdited(): void {
    if (this.passwordProblems().length > 0) {
      this.passwordProblems.set([]);
    }
  }

  protected async submit(): Promise<void> {
    this.submitAttempted.set(true);

    const link = this.link;

    if (this.form.invalid || this.submitting() || !link) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);
    this.passwordProblems.set([]);

    try {
      await this.auth.resetPassword({
        userId: link.userId,
        token: link.token,
        newPassword: this.form.controls.password.value,
      });

      this.forget();
      this.form.reset();
      this.state.set('done');
      this.focusAfterRender('h1');
    } catch (error) {
      this.refused(error);
    } finally {
      this.submitting.set(false);
    }
  }

  private refused(error: unknown): void {
    const failure = toApiError(error);

    // Expired, used, or never valid. Retrying cannot help, so the link is
    // forgotten and the reader is offered a new one.
    if (failure.code === RESET_INVALID_CODE) {
      this.forget();
      this.form.reset();
      this.state.set('expired');
      this.focusAfterRender('h1');

      return;
    }

    const problems = refusedFields(failure, ['newPassword']).has('newPassword')
      ? [this.passwordError]
      : passwordProblemLines(failure);

    // The link was accepted and the password was not. The link is kept, so the
    // reader can choose another password with it straight away.
    if (problems.length > 0) {
      this.passwordProblems.set(problems);
      this.form.reset();
      this.submitAttempted.set(false);
      this.focusAfterRender('#password');

      return;
    }

    this.failure.set(
      describeResetFailure(failure, {
        unavailable: $localize`:@@resetPassword.unavailable:Password reset is temporarily unavailable. Your password was not changed. Try again in a few minutes.`,
        generic: $localize`:@@resetPassword.failed:Something went wrong, and your password was not changed. Try again in a moment.`,
      }),
    );
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
