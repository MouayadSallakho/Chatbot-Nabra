import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  input,
  output,
  signal,
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
      (click)="onDialogClick($event)"
    >
      <!--
        Rendered only while open, so a closed dialog adds no buttons to the
        page: nothing can find, count or announce "Revoke invitation" on a page
        that is not asking.
      -->
      @if (isOpen()) {
        <h2 [id]="headingId" class="text-section-title">{{ heading() }}</h2>
        <p class="confirm-body text-small text-secondary">{{ body() }}</p>

        <div class="confirm-actions">
          <button #cancelButton type="button" class="btn btn-secondary" (click)="close(false)">
            {{ cancelLabel() }}
          </button>
          <button
            type="button"
            class="btn"
            [class.btn-danger]="tone() === 'danger'"
            [class.btn-primary]="tone() !== 'danger'"
            (click)="close(true)"
          >
            {{ confirmLabel() }}
          </button>
        </div>
      }
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

  private settle: ((accepted: boolean) => void) | null = null;

  protected readonly isOpen = signal(false);

  private readonly injector = inject(Injector);
  private readonly cancelButton = viewChild<ElementRef<HTMLButtonElement>>('cancelButton');

  /**
   * Opens the dialog modally.
   *
   * Focus lands on the safe answer, once it has rendered: a reader who presses
   * Enter without reading has declined, not confirmed.
   */
  open(): void {
    this.accepted = false;
    this.isOpen.set(true);
    this.dialog().nativeElement.showModal();

    afterNextRender(() => this.cancelButton()?.nativeElement.focus(), {
      injector: this.injector,
    });
  }

  /**
   * Opens the dialog and resolves with the reader's answer.
   *
   * For call sites that decide inline -- `if (!(await dialog.ask())) return;`
   * -- instead of splitting one action across two event handlers.
   */
  ask(): Promise<boolean> {
    this.open();

    return new Promise((resolve) => (this.settle = resolve));
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
    this.isOpen.set(false);

    if (this.accepted) {
      this.confirmed.emit();
    } else {
      this.cancelled.emit();
    }

    this.settle?.(this.accepted);
    this.settle = null;
  }

  /**
   * A press on the backdrop cancels. The <dialog> element is the backdrop's
   * hit target too, so a click whose target is the dialog itself -- not
   * anything inside it -- landed outside the visible box.
   */
  protected onDialogClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.close(false);
    }
  }
}
