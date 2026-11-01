import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toApiError } from '@app/core/api/api-error';
import { AuthService } from '@app/core/auth/auth-service';
import { LOGIN_PATH } from '@app/core/auth/auth-guard';
import { InvitationHandoff } from '@app/core/invitations/invitation-handoff';
import { Icon } from '@app/shared/icon/icon';
import { EntryFrame } from '@app/layout/entry-frame/entry-frame';
import { describeResetFailure } from '../password-reset-failure';

/** Asking for the link, or told it is on its way. */
type ForgotState = 'asking' | 'sent';

/**
 * The page where somebody who cannot sign in asks for a link to set a new
 * password.
 *
 * <strong>One answer, whoever asks.</strong> The server replies 202 whether or
 * not an account uses the address, and this page says the same sentence either
 * way: "if an account uses that address, we have sent it a link". Any
 * difference -- a different sentence, a different heading, a faster or slower
 * screen -- would turn the form into a way to find out who has an account.
 *
 * <strong>The refusals that are not about the address are said honestly.</strong>
 * Too many requests, or the service unavailable, mean nothing was sent, and
 * showing the confirmation then would leave somebody waiting for an email that
 * is never coming. Neither depends on whether the address has an account: the
 * server counts requests for unknown addresses too.
 */
@Component({
  selector: 'app-forgot-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EntryFrame, ReactiveFormsModule, RouterLink, Icon],
  templateUrl: './forgot-password.html',
  styleUrl: '../auth-page.scss',
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);
  private readonly invitations = inject(InvitationHandoff);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  protected readonly loginPath = `/${LOGIN_PATH}`;

  /**
   * Whether an invitation waits in memory. Somebody can reach this page from
   * sign-in in the middle of accepting one, and the language switch's reload
   * would lose it.
   */
  protected readonly invitationPending = this.invitations.pending;

  protected readonly state = signal<ForgotState>('asking');
  protected readonly submitting = signal(false);
  protected readonly failure = signal<string | null>(null);

  /** The address the link was asked for, repeated so a typo can be noticed. */
  protected readonly address = signal('');

  private readonly submitAttempted = signal(false);

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),
  });

  protected showError(): boolean {
    const control = this.form.controls.email;

    return control.invalid && (control.touched || this.submitAttempted());
  }

  protected async submit(): Promise<void> {
    this.submitAttempted.set(true);

    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

      return;
    }

    const email = this.form.controls.email.value.trim();

    this.submitting.set(true);
    this.failure.set(null);

    try {
      await this.auth.forgotPassword(email);

      this.address.set(email);
      this.state.set('sent');
      this.focusHeading();
    } catch (error) {
      this.failure.set(
        describeResetFailure(toApiError(error), {
          unavailable: $localize`:@@forgotPassword.unavailable:Password reset is temporarily unavailable. Nothing was sent. Try again in a few minutes.`,
          generic: $localize`:@@forgotPassword.failed:Something went wrong, and no link was requested. Try again in a moment.`,
        }),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  /** Back to the form, keeping the address so a typo can be corrected. */
  protected tryAnotherAddress(): void {
    this.state.set('asking');
    this.submitAttempted.set(false);
    this.focusAfterRender('#email');
  }

  /**
   * Moves focus to the new state's heading, so a keyboard reader is not left on
   * a button that no longer exists and a screen reader announces what changed.
   */
  private focusHeading(): void {
    this.focusAfterRender('h1');
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
