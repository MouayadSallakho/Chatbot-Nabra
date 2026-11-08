import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { LOGIN_PATH } from '@app/core/auth/auth-guard';
import {
  ACCEPT_INVITATION_PATH,
  InvitationHandoff,
} from '@app/core/invitations/invitation-handoff';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { APP_LANGUAGE } from '@app/core/i18n/language';
import { PASSWORD_MIN_LENGTH } from '@app/core/auth/password-policy';
import { passwordProblemLines } from '@app/core/problems/core-problems';
import { refusedFields } from '@app/core/problems/field-problems';
import { Icon } from '@app/shared/icon/icon';
import { EntryFrame } from '@app/layout/entry-frame/entry-frame';

/** The fields of this form, by the names the server uses for them too. */
const FIELDS = ['email', 'password'] as const;
type Field = (typeof FIELDS)[number];

/**
 * The registration screen.
 *
 * <strong>It creates an account and nothing else.</strong> No workspace, no
 * role, no permission. The request body is the address, the password and the
 * page's language (for the account's emails), so there is no way for this form
 * to ask for anything it should not have, and choosing a workspace is a
 * separate, deliberate step on the screen after it.
 *
 * <strong>The password never leaves the form control.</strong> It goes into the
 * request body and is cleared on failure. Nothing writes it to a signal, a
 * field, browser storage or a log.
 */
@Component({
  selector: 'app-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EntryFrame, ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './register.html',
  styleUrl: '../auth-page.scss',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly invitations = inject(InvitationHandoff);
  private readonly router = inject(Router);

  /** The language this page is in, which the account's emails will be written in. */
  private readonly language = inject(APP_LANGUAGE);

  protected readonly loginPath = LOGIN_PATH;

  protected readonly passwordHint = $localize`:@@register.password.hint:At least ${PASSWORD_MIN_LENGTH}:count: characters. A memorable phrase works well.`;
  protected readonly passwordError = $localize`:@@register.password.error:Use at least ${PASSWORD_MIN_LENGTH}:count: characters.`;

  /** Whether this registration is a step inside accepting an invitation. */
  protected readonly invitationPending = this.invitations.pending;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly passwordVisible = signal(false);

  /** One sentence per password rule the server said was broken. */
  protected readonly passwordProblems = signal<readonly string[]>([]);

  /** Fields a refusal named, until the reader edits them. */
  protected readonly refused = signal<ReadonlySet<Field>>(new Set());

  private readonly submitAttempted = signal(false);

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH)],
    }),
  });

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

    if (field === 'password' && this.passwordProblems().length > 0) {
      this.passwordProblems.set([]);
    }
  }

  protected togglePassword(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  protected async submit(): Promise<void> {
    this.submitAttempted.set(true);

    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);
    this.passwordProblems.set([]);
    this.refused.set(new Set());

    try {
      await this.auth.register({
        email: this.form.controls.email.value.trim(),
        password: this.form.controls.password.value,
        language: this.language,
      });

      // A brand new account belongs to no workspace. The list is loaded anyway
      // rather than assumed, which is what makes the invitation case below work:
      // registering to accept an invitation is now a real path, and the note that
      // used to say "wrong the moment invitations exist" describes today.
      await this.workspace.load();

      // Registering was a step inside accepting an invitation, so the reader goes
      // back to finish it rather than to a workspace-creation screen they did not
      // ask for.
      if (this.invitations.pending()) {
        await this.router.navigate(['/', ACCEPT_INVITATION_PATH]);

        return;
      }

      await this.router.navigate(['/', WORKSPACE_SETUP_PATH]);
    } catch (error) {
      // The backend stays the authority. A password it refused is shown beside
      // the password, one line per rule; anything else above the form.
      const failure = error as ApiError;
      const problems = passwordProblemLines(failure);

      this.passwordProblems.set(problems);
      this.failure.set(problems.length > 0 ? null : failure);
      this.refused.set(refusedFields(failure, FIELDS));
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }
}
