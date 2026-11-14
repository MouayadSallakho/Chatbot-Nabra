import {
  DestroyRef,
  Directive,
  ElementRef,
  Renderer2,
  RendererStyleFlags2,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';

let nextId = 0;

/** The space between a window-placed tooltip and its trigger (`--space-2`). */
const TOOLTIP_GAP_PX = 8;

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
 * its end side (the collapsed side bar), `above` over it, `below` under it
 * (the phone's top bar).
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

  /** Where it sits: beside the trigger on its end side, above it, or below it. */
  readonly placement = input<'end' | 'above' | 'below'>('end');

  /** Whether the words add to the trigger's name (then `aria-describedby`). */
  readonly describes = input(false);

  /**
   * Placed against the window rather than inside the trigger (39.3): for a
   * trigger in a scrolling list whose tooltip must sit outside the list (the
   * collapsed side bar), where an absolutely placed tooltip would be clipped by
   * the list. Measured from the trigger each time it opens; `end` placement.
   */
  readonly fixed = input(false);

  /**
   * With `fixed`: an ancestor (a selector) whose end edge the tooltip clears,
   * rather than the trigger's (the rail's edge, not its 40px item's).
   */
  readonly edge = input<string | null>(null);

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
    // No words, no tooltip: a trigger that names itself visibly in one layout
    // (the full side bar) and not in another (the rail) passes '' for the first.
    const shown = this.appTooltip().trim() !== '';

    if (shown && this.fixed()) {
      this.placeAgainstWindow();
    }

    this.open.set(shown);
  }

  /** Beside the trigger on its end side, centred on it, in window coordinates. */
  private placeAgainstWindow(): void {
    const trigger = this.host.nativeElement;
    const box = trigger.getBoundingClientRect();
    const selector = this.edge();
    const edgeBox = ((selector && trigger.closest(selector)) || trigger).getBoundingClientRect();
    const view = trigger.ownerDocument.documentElement;
    const rtl = getComputedStyle(trigger).direction === 'rtl';
    const gap = TOOLTIP_GAP_PX;
    const el = this.element;

    this.renderer.setAttribute(el, 'data-fixed', '');
    this.renderer.setStyle(
      el,
      'inset-block-start',
      `${box.top + box.height / 2}px`,
      RendererStyleFlags2.DashCase,
    );
    this.renderer.setStyle(
      el,
      'inset-inline-start',
      `${rtl ? view.clientWidth - edgeBox.left + gap : edgeBox.right + gap}px`,
      RendererStyleFlags2.DashCase,
    );
  }

  protected hide(): void {
    this.open.set(false);
  }
}
