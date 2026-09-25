import {
  DestroyRef,
  Directive,
  ElementRef,
  afterNextRender,
  inject,
  input,
  signal,
} from '@angular/core';

/**
 * Makes a horizontally scrolling container usable from the keyboard.
 *
 * <strong>The defect it closes.</strong> A wide table inside an
 * `overflow-x: auto` wrapper can only be scrolled with a pointer: the cells are
 * not focusable, so a keyboard user can reach neither the hidden columns nor the
 * scrollbar. This is WCAG 2.1.1 and axe's `scrollable-region-focusable`.
 *
 * <strong>Only while it actually overflows.</strong> When the content fits there
 * is nothing to scroll, so the container gets no tab stop and no landmark — a
 * focusable, named region around every small table would be noise for every
 * keyboard and screen-reader user. When it overflows (a phone, a long table) it
 * becomes `tabindex="0"`, `role="region"` and is named by `appScrollRegion`, so
 * arrow keys scroll it and a screen reader announces what it holds. Focus is
 * drawn by the global `:focus-visible` ring.
 *
 * Measured with a `ResizeObserver` on the container and its content, so a
 * rotation, a resized window or rows arriving later are all noticed. Where no
 * observer exists (a test runner, a server render) the container is left as
 * plain markup.
 */
@Directive({
  selector: '[appScrollRegion]',
  host: {
    '[attr.tabindex]': 'overflowing() ? 0 : null',
    '[attr.role]': 'overflowing() ? "region" : null',
    '[attr.aria-label]': 'overflowing() ? label() : null',
  },
})
export class ScrollRegion {
  /** What the region holds, for its accessible name. Required: an unnamed region is not one. */
  readonly label = input.required<string>({ alias: 'appScrollRegion' });

  protected readonly overflowing = signal(false);

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    const destroyRef = inject(DestroyRef);

    afterNextRender(() => {
      if (typeof ResizeObserver === 'undefined') {
        return;
      }

      const host = this.element.nativeElement;
      const measure = () => this.overflowing.set(host.scrollWidth > host.clientWidth + 1);
      const observer = new ResizeObserver(measure);

      observer.observe(host);

      for (const child of Array.from(host.children)) {
        observer.observe(child);
      }

      measure();
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }
}
