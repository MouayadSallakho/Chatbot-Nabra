import { DestroyRef, WritableSignal, afterNextRender, inject } from '@angular/core';

/**
 * Keeps `into` equal to an element's width, in whole pixels.
 *
 * Charts draw in pixels so text is never stretched and marks keep their fixed
 * thickness; this is how they learn how many pixels they have. Call it in an
 * injection context: it measures after the first render, follows resizes, and
 * disconnects when the component goes. Where there is no `ResizeObserver`
 * (unit tests in jsdom) the default width simply stays.
 */
export function measureWidth(
  target: () => HTMLElement | null | undefined,
  into: WritableSignal<number>,
): void {
  const destroyRef = inject(DestroyRef);

  afterNextRender(() => {
    const element = target();

    if (!element) {
      return;
    }

    if (element.clientWidth > 0) {
      into.set(element.clientWidth);
    }

    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver((entries) => {
      const next = Math.round(entries[0]?.contentRect.width ?? 0);

      if (next > 0 && next !== into()) {
        into.set(next);
      }
    });

    observer.observe(element);
    destroyRef.onDestroy(() => observer.disconnect());
  });
}
