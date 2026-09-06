import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { REGISTER_PATH } from '@app/core/auth/auth-guard';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { HOME_PATH } from '@app/layout/navigation';
import { Icon } from '@app/shared/icon/icon';

/**
 * The sign-in screen.
 *
 * <strong>The password is never held.</strong> It lives in a form control until
 * submit, goes into the request body, and the control is cleared on failure.
 * Nothing writes it to a signal, a field, `localStorage`, `sessionStorage` or a
 * log, and nothing reads it back.
 *
 * <strong>The failure message is the server's own, verbatim.</strong> The
 * backend answers a wrong password, an unknown address and a locked account with
 * one deliberately uninformative sentence, so that this form cannot be used to
 * discover which addresses are registered. Re-wording it here — "no account with
 * that email", say — would undo that on the client side, so this screen shows
 * exactly what it was given.
 */
@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './login.html',
  styleUrl: '../auth-page.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly router = inject(Router);

  protected readonly registerPath = REGISTER_PATH;

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
      validators: [Validators.required],
    }),
  });

  /**
   * Whether a field's error belongs on screen yet.
   *
   * Only after the reader has left the field or tried to submit. Marking a
   * required field invalid while somebody is typing its first character is
   * accurate and reads as nagging.
   */
  protected showError(field: 'email' | 'password'): boolean {
    const control = this.form.controls[field];

    return control.invalid && (control.touched || this.submitAttempted());
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
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

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

      await this.router.navigate([
        '/',
        this.workspace.isReady() ? HOME_PATH : WORKSPACE_SETUP_PATH,
      ]);
    } catch (error) {
      this.failure.set(error as ApiError);

      // The password is wrong, or the account is locked, or it does not exist.
      // Whichever it was, this copy is no longer wanted: leaving it in the field
      // invites the same failed attempt again and leaves it on screen if the
      // reader walks away.
      this.form.controls.password.reset();
    } finally {
      this.submitting.set(false);
    }
  }
}
