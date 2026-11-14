/** The most blurred (glass) layers allowed on screen at once (calm-glass.md §3). */
export const GLASS_LAYER_BUDGET = 3;

/**
 * How many blurred layers are on screen now: rendered elements whose computed
 * `backdrop-filter` is not `none` and that intersect the viewport.
 *
 * The design lab shows this number beside the budget, and the Chrome checks
 * of each step read it, so "at most three" is measured rather than promised.
 * Under "Reduce transparency" (the setting or the OS preference) and forced
 * colours every glass resolves to its solid twin, so the count is 0.
 */
export function countBlurredLayers(root: Document = document): number {
  const view = root.defaultView;

  if (!view) {
    return 0;
  }

  let count = 0;

  for (const element of Array.from(root.querySelectorAll<HTMLElement>('body *'))) {
    const style = view.getComputedStyle(element);
    const filter = style.getPropertyValue('backdrop-filter');

    if (!filter || filter === 'none' || style.display === 'none' || style.visibility === 'hidden') {
      continue;
    }

    const box = element.getBoundingClientRect();
    const onScreen =
      box.width > 0 &&
      box.height > 0 &&
      box.bottom > 0 &&
      box.right > 0 &&
      box.top < view.innerHeight &&
      box.left < view.innerWidth;

    if (onScreen) {
      count += 1;
    }
  }

  return count;
}
