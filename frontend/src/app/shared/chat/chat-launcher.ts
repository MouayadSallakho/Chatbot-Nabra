import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  output,
} from '@angular/core';

/**
 * The closed widget: one round button in the workspace's colour (Phase 36.9).
 *
 * It is exactly the 56px circle the loader's 84px closed frame was measured
 * for (16px from the corner, room for its shadow), so the frame geometry and
 * the bridge messages are unchanged. Its colour arrives as `--chat-accent`,
 * already checked; a colour too pale to see on a white page also gets
 * `--chat-accent-edge`, a 1px outline in its own label colour.
 *
 * Its name says what it opens and who answers ("Chat with Northwind
 * Concierge"); the glyph is decoration.
 */
@Component({
  selector: 'app-chat-launcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.is-left]': 'side() === "Left"' },
  template: `
    <button
      type="button"
      class="launcher"
      [attr.aria-label]="label()"
      (click)="preview() || pressed.emit()"
      data-testid="chat-launcher"
    >
      <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <path
          d="M12 3c-4.97 0-9 3.36-9 7.5 0 2.3 1.25 4.35 3.2 5.71-.13 1.2-.6 2.3-1.38 3.2a.5.5 0 0 0 .45.83c1.9-.3 3.4-1.05 4.5-1.86.72.14 1.47.22 2.23.22 4.97 0 9-3.36 9-7.5S16.97 3 12 3Z"
          fill="currentColor"
        />
      </svg>
    </button>
  `,
  styles: `
    /* Physical on purpose: the corner is a physical setting ("Bottom right"),
       and the loader puts the frame in that physical corner in either language. */
    :host {
      position: absolute;
      inset-block-end: 16px;
      right: 16px; /* rtl-lint-ignore: the workspace's physical corner */
    }

    :host(.is-left) {
      right: auto; /* rtl-lint-ignore: the workspace's physical corner */
      left: 16px; /* rtl-lint-ignore: the workspace's physical corner */
    }

    .launcher {
      display: grid;
      place-items: center;
      inline-size: 56px;
      block-size: 56px;
      padding: 0;
      border: 0;
      border-radius: 50%;
      background-color: var(--chat-accent);
      /* A neutral lift on somebody else's page, the same in either theme. */
      box-shadow:
        0 0 0 1px var(--chat-accent-edge, transparent),
        0 2px 6px rgb(0 0 0 / 18%) /* token-lint-ignore */,
        0 8px 20px -6px rgb(0 0 0 / 30%) /* token-lint-ignore */;
      color: var(--chat-on-accent);
      cursor: pointer;
      transition: transform var(--duration-sm) var(--ease-standard);

      svg {
        inline-size: 26px;
        block-size: 26px;
      }

      &:hover:not(:disabled) {
        transform: scale(1.04);
      }

      &:active:not(:disabled) {
        transform: scale(0.97);
      }

      // Two rings: the label colour against the accent, then the accent against
      // whatever page sits behind the frame.
      &:focus-visible {
        outline: 2px solid var(--chat-on-accent);
        outline-offset: -5px;
        box-shadow:
          0 0 0 3px var(--chat-accent),
          0 2px 6px rgb(0 0 0 / 18%) /* token-lint-ignore: the same lift */;
      }

      &:disabled {
        cursor: default;
      }
    }

    @media (forced-colors: active) {
      .launcher {
        border: 2px solid ButtonText;
      }
    }
  `,
})
export class ChatLauncher {
  /** Read by a parent that returns focus here when the panel closes. */
  readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly label = input.required<string>();
  readonly side = input<'Left' | 'Right'>('Right');
  readonly preview = input(false);

  readonly pressed = output<void>();

  focus(): void {
    this.element.nativeElement.querySelector('button')?.focus();
  }
}
