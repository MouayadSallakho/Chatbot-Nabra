import {
  DestroyRef,
  Directive,
  ElementRef,
  Renderer2,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';

let nextId = 0;

/**
 * A small glass tooltip on hover and on focus (calm-glass.md §6;
 * plan 39.0: the names of the collapsed side bar's areas, 39.3).
 *
 * ```html
 * <a routerLink="/inbox" aria-label="Inbox" appTooltip="Inbox">…</a>
 * ```
 *
 * WCAG 1.4.13: it appears on hover and on any focus (a pointer has already
 * shown it by hovering, so no modality check is needed), stays while the pointer is
 * over it or the trigger keeps focus, and Escape dismisses it without moving
 * focus. It is drawn inside the trigger, hidden from the accessibility tree
 * (`aria-hidden`), so it never changes the trigger's name; when it says more
 * than the name, `describes` links it with `aria-describedby`.
 *
 * It names or explains; it never holds the only copy of something a person
 * needs, so a touch screen (no hover) loses nothing. The trigger is made a
 * positioning box. `placement` is logical: `end` sits beside the trigger on
 * its end side (the collapsed side bar), `above` over it.
 */
@Directive({
  selector: '[appTooltip]',
  host: {
    '[style.position]': "'relative'",
    '(mouseenter)': 'show()',
    '(mouseleave)': 'hide()',
    '(focusin)': 'show()',
    '(focusout)': 'hide()',
    '(keydown.escape)': 'hide()',
    '[attr.aria-describedby]': 'describes() ? id : null',
  },
})
export class Tooltip {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly renderer = inject(Renderer2);

  /** The words. */
  readonly appTooltip = input.required<string>();

  /** Where it sits: beside the trigger on its end side, or above it. */
  readonly placement = input<'end' | 'above'>('end');

  /** Whether the words add to the trigger's name (then `aria-describedby`). */
  readonly describes = input(false);

  protected readonly id = `tooltip-${++nextId}`;

  /** Whether it is on screen now. */
  readonly open = signal(false);

  private readonly element: HTMLElement = this.renderer.createElement('span');

  constructor() {
    const el = this.element;
    this.renderer.addClass(el, 'tooltip');
    this.renderer.setAttribute(el, 'role', 'tooltip');
    this.renderer.setAttribute(el, 'aria-hidden', 'true');
    this.renderer.setAttribute(el, 'id', this.id);
    this.renderer.setAttribute(el, 'hidden', '');

    effect(() => {
      this.renderer.setProperty(el, 'textContent', this.appTooltip());
      this.renderer.setAttribute(el, 'data-placement', this.placement());
    });

    effect(() => {
      if (this.open()) {
        this.renderer.removeAttribute(el, 'hidden');
      } else {
        this.renderer.setAttribute(el, 'hidden', '');
      }
    });

    this.renderer.appendChild(this.host.nativeElement, el);
    inject(DestroyRef).onDestroy(() => el.remove());
  }

  protected show(): void {
    this.open.set(true);
  }

  protected hide(): void {
    this.open.set(false);
  }
}
