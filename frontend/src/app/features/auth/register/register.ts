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
import { Icon } from '@app/shared/icon/icon';

/**
 * The minimum password length, mirroring the server's policy.
 *
 * <strong>A copy, and only for immediate feedback.</strong> The server owns the
 * policy and re-checks it; this exists so somebody typing a short password is
 * told before they submit rather than after a round trip. If the two ever
 * disagree, the server wins and its message is what appears.
 */
const PASSWORD_MIN_LENGTH = 12;

/**
 * The registration screen.
 *
 * <strong>It creates an account and nothing else.</strong> No workspace, no
 * role, no permission. The request body has two fields, so there is no way for
 * this form to ask for anything it should not have, and choosing a workspace is
 * a separate, deliberate step on the screen after it.
 *
 * <strong>The password never leaves the form control.</strong> It goes into the
 * request body and is cleared on failure. Nothing writes it to a signal, a
 * field, browser storage or a log.
 */
@Component({
  selector: 'app-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './register.html',
  styleUrl: '../auth-page.scss',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly invitations = inject(InvitationHandoff);
  private readonly router = inject(Router);

  protected readonly loginPath = LOGIN_PATH;
  protected readonly minLength = PASSWORD_MIN_LENGTH;

  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly passwordVisible = signal(false);

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

  protected showError(field: 'email' | 'password'): boolean {
    const control = this.form.controls[field];

    return control.invalid && (control.touched || this.submitAttempted());
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

    try {
      await this.auth.register({
        email: this.form.controls.email.value.trim(),
        password: this.form.controls.password.value,
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
      // The backend stays the authority. A taken address and a password that
      // breaks the policy are reported in its own words rather than re-derived.
      this.failure.set(error as ApiError);
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }
}
