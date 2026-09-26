import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';

/** The item roles the menu moves focus between. */
const ITEM_SELECTOR =
  '[role="menuitem"]:not([disabled]), [role="menuitemradio"]:not([disabled]), [role="menuitemcheckbox"]:not([disabled])';

let nextId = 0;

/**
 * A menu button: a trigger and the menu it opens.
 *
 * The one implementation of the pattern, so every menu in the product behaves
 * the same way -- the profile menu, a row's overflow menu, anything later:
 *
 * - opening moves focus to the first item; the arrow keys, Home and End move
 *   between items;
 * - Escape closes it and returns focus to the trigger; so does choosing an item;
 * - Tab closes it and lets focus continue, as it would from any control;
 * - a pointer press outside closes it without moving focus;
 * - it opens towards whichever side has room, so it never leaves the viewport.
 *
 * Items are projected as ordinary buttons and links carrying `role="menuitem"`
 * (or `menuitemradio`), so what an item does stays the caller's own code.
 * Nothing here needs hover: every path is reachable by keyboard and by touch.
 */
@Component({
  selector: 'app-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      #trigger
      type="button"
      [class]="triggerClass()"
      [id]="triggerId"
      aria-haspopup="menu"
      [attr.aria-expanded]="open()"
      [attr.aria-controls]="open() ? menuId : null"
      [attr.aria-label]="label() || null"
      (click)="toggle()"
      (keydown.arrowDown)="openFromKeyboard($event, 'first')"
      (keydown.arrowUp)="openFromKeyboard($event, 'last')"
    >
      <ng-content select="[menuTrigger]" />
    </button>

    @if (open()) {
      <div
        #panel
        class="menu"
        role="menu"
        [id]="menuId"
        [attr.aria-labelledby]="triggerId"
        [class.menu-align-start]="side() === 'start'"
        [class.menu-align-end]="side() === 'end'"
        [class.menu-above]="above()"
        (keydown)="onKeydown($event)"
        (click)="onItemClick($event)"
      >
        <ng-content />
      </div>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: inline-flex;
    }

    .menu {
      inset-block-start: calc(100% + var(--space-1));
    }

    .menu-align-end {
      inset-inline-end: 0;
    }

    .menu-align-start {
      inset-inline-start: 0;
    }

    .menu-above {
      inset-block-start: auto;
      inset-block-end: calc(100% + var(--space-1));
    }
  `,
  host: {
    '(document:pointerdown)': 'onDocumentPointerDown($event)',
  },
})
export class Menu {
  /** Accessible name for the trigger, when its visible content is not one. */
  readonly label = input('');

  /** Classes for the trigger button: it is styled as whatever button it is. */
  readonly triggerClass = input('btn btn-ghost btn-icon btn-sm');

  /**
   * Which edge of the trigger the menu lines up with, when there is room.
   *
   * Not called `align`: that is a legacy HTML attribute, and Chrome still maps
   * it to `text-align` on the host element, which every item then inherited.
   */
  readonly placement = input<'start' | 'end'>('end');

  protected readonly open = signal(false);
  protected readonly side = signal<'start' | 'end'>('end');
  protected readonly above = signal(false);

  protected readonly triggerId = `menu-trigger-${++nextId}`;
  protected readonly menuId = `menu-${nextId}`;

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');

  /** Closes the menu. Focus returns to the trigger unless told otherwise. */
  close(restoreFocus = true): void {
    if (!this.open()) {
      return;
    }

    this.open.set(false);

    if (restoreFocus) {
      this.trigger().nativeElement.focus();
    }
  }

  protected toggle(): void {
    if (this.open()) {
      this.close();
    } else {
      this.show('first');
    }
  }

  protected openFromKeyboard(event: Event, focus: 'first' | 'last'): void {
    event.preventDefault();
    this.show(focus);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const items = this.items();
    const current = items.indexOf(document.activeElement as HTMLElement);

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        items[(current + 1) % items.length]?.focus();
        break;
      case 'ArrowUp':
        event.preventDefault();
        items[(current - 1 + items.length) % items.length]?.focus();
        break;
      case 'Home':
        event.preventDefault();
        items[0]?.focus();
        break;
      case 'End':
        event.preventDefault();
        items[items.length - 1]?.focus();
        break;
      case 'Escape':
        event.preventDefault();
        // Stops the shell's own Escape handling from also closing a drawer or
        // the peeking sidebar underneath: one Escape, one layer.
        event.stopPropagation();
        this.close();
        break;
      case 'Tab':
        this.close(false);
        break;
    }
  }

  /** Choosing an item closes the menu, and focus goes back where it came from. */
  protected onItemClick(event: MouseEvent): void {
    const item = (event.target as HTMLElement).closest(ITEM_SELECTOR);

    if (item) {
      // A link navigates, and the page it opens takes focus; a button leaves the
      // reader where they were, so the trigger gets it back.
      this.close(item.tagName !== 'A');
    }
  }

  protected onDocumentPointerDown(event: PointerEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.close(false);
    }
  }

  private show(focus: 'first' | 'last'): void {
    this.side.set(this.placement());
    this.above.set(false);
    this.open.set(true);

    afterNextRender(
      () => {
        this.fitToViewport();
        const items = this.items();
        (focus === 'first' ? items[0] : items[items.length - 1])?.focus();
      },
      { injector: this.injector },
    );
  }

  /**
   * Flips the menu to the side that has room. Measured after rendering rather
   * than guessed from the trigger's position, so it holds for any menu width and
   * any zoom level.
   */
  private fitToViewport(): void {
    const panel = this.panel()?.nativeElement;

    if (!panel) {
      return;
    }

    const rect = panel.getBoundingClientRect();
    const view = document.documentElement;

    if (rect.left < 0 || rect.right > view.clientWidth) {
      this.side.set(this.side() === 'end' ? 'start' : 'end');
    }

    const triggerRect = this.trigger().nativeElement.getBoundingClientRect();

    if (
      rect.bottom > view.clientHeight &&
      triggerRect.top > view.clientHeight - triggerRect.bottom
    ) {
      this.above.set(true);
    }
  }

  private items(): HTMLElement[] {
    return [...(this.panel()?.nativeElement.querySelectorAll<HTMLElement>(ITEM_SELECTOR) ?? [])];
  }
}
