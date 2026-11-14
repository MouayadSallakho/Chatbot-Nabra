/** What Tab stops on inside a popover or a sheet. */
const TAB_STOPS =
  'a[href], button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Keeps Tab inside an open popover or sheet (Phase 39.3, calm-glass.md §6:
 * "focus trapped"): from its last stop Tab goes back to its first, and Shift+Tab
 * from its first (or from its heading, which is not a stop) to its last. Every
 * other Tab is the browser's own.
 *
 * Call it from the container's `keydown`. A native modal `<dialog>` already
 * keeps focus off the page behind it, but lets it leave for the browser's own
 * toolbar after the last control; this closes that gap too.
 */
export function trapTab(event: KeyboardEvent, container: HTMLElement | null | undefined): void {
  if (event.key !== 'Tab' || !container) {
    return;
  }

  const stops = [...container.querySelectorAll<HTMLElement>(TAB_STOPS)].filter(
    (element) => !element.closest('[hidden]'),
  );

  if (stops.length === 0) {
    event.preventDefault();

    return;
  }

  const first = stops[0];
  const last = stops[stops.length - 1];
  const active = container.ownerDocument.activeElement as HTMLElement | null;
  const inside = active !== null && stops.includes(active);

  if (event.shiftKey && (active === first || !inside)) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && (active === last || !inside)) {
    event.preventDefault();
    first.focus();
  }
}
