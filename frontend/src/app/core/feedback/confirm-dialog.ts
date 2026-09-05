import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';

/**
 * A confirmation before an action worth pausing on.
 *
 * <strong>Built on the native `&lt;dialog&gt;` element</strong>, and that is the
 * whole design. `showModal()` gives a real focus trap, Escape-to-close, the
 * inert backdrop, the top layer and the correct `role="dialog"` with
 * `aria-modal` — every one of which a hand-rolled overlay has to reimplement,
 * and most of which hand-rolled overlays get wrong. No UI framework was added
 * for this.
 *
 * The accessible name comes from `aria-labelledby` pointing at the heading, so
 * it cannot drift from what the reader sees.
 *
 * Focus return is the browser's: closing a modal `&lt;dialog&gt;` restores focus
 * to whatever opened it.
 */
@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #dialog
      class="confirm"
      [attr.aria-labelledby]="headingId"
      (close)="onNativeClose()"
      (cancel)="cancelled.emit()"
    >
      <h2 [id]="headingId" class="text-section-title">{{ heading() }}</h2>
      <p class="confirm-body text-small text-secondary">{{ body() }}</p>

      <div class="confirm-actions">
        <button type="button" class="btn btn-secondary btn-sm" (click)="close(false)">
          {{ cancelLabel() }}
        </button>
        <button
          type="button"
          class="btn btn-sm"
          [class.btn-danger]="tone() === 'danger'"
          [class.btn-primary]="tone() !== 'danger'"
          (click)="close(true)"
        >
          {{ confirmLabel() }}
        </button>
      </div>
    </dialog>
  `,
  styleUrl: './confirm-dialog.scss',
})
export class ConfirmDialog {
  readonly heading = input.required<string>();
  readonly body = input('');
  readonly confirmLabel = input('Confirm');
  readonly cancelLabel = input('Cancel');

  /** Whether the confirmed action is destructive, which changes the button. */
  readonly tone = input<'default' | 'danger'>('default');

  /** The reader confirmed. */
  readonly confirmed = output<void>();

  /** The reader declined, by button, Escape or backdrop. */
  readonly cancelled = output<void>();

  protected readonly headingId = `confirm-${Math.random().toString(36).slice(2, 9)}`;

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  /** Whether the last close was a confirmation, so `close` can report correctly. */
  private accepted = false;

  /** Opens the dialog modally. */
  open(): void {
    this.accepted = false;
    this.dialog().nativeElement.showModal();
  }

  protected close(accepted: boolean): void {
    this.accepted = accepted;
    this.dialog().nativeElement.close();
  }

  /**
   * The single place either outcome is reported.
   *
   * Wired to the native `close` event rather than to the buttons, so that
   * dismissing with Escape — which the browser handles without telling the
   * buttons anything — is reported exactly like pressing Cancel.
   */
  protected onNativeClose(): void {
    if (this.accepted) {
      this.confirmed.emit();
    } else {
      this.cancelled.emit();
    }
  }
}
