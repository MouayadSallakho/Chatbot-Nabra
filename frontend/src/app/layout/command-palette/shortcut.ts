/**
 * The command palette's keyboard shortcut: Ctrl+K, or Command+K on Apple
 * devices.
 *
 * Kept apart from the palette itself, which is loaded only when first opened:
 * the shell needs to recognise the key and label the trigger from the start.
 */

/** Whether the platform's command key is Command rather than Control. */
export function isApplePlatform(
  nav: Pick<Navigator, 'platform' | 'userAgent'> | undefined,
): boolean {
  return !!nav && /Mac|iPhone|iPad|iPod/.test(nav.platform || nav.userAgent);
}

/** The printed shortcut. Key names are Latin on every keyboard, so never translated. */
export function shortcutLabel(apple: boolean): string {
  return apple ? '⌘K' : 'Ctrl K';
}

/** The `aria-keyshortcuts` value for the trigger. */
export function shortcutAria(apple: boolean): string {
  return apple ? 'Meta+K' : 'Control+K';
}

/**
 * Whether a key press is the palette shortcut.
 *
 * Matched on the physical key (`code`) as well as the character, so it works
 * with an Arabic layout active, where the K key types "ن". Only the platform's
 * own modifier counts: Control+K on a Mac is a text-editing command (delete to
 * the end of the line) and must keep doing that.
 */
export function isPaletteShortcut(event: KeyboardEvent, apple: boolean): boolean {
  if (event.altKey || event.shiftKey) {
    return false;
  }

  const modifier = apple ? event.metaKey && !event.ctrlKey : event.ctrlKey && !event.metaKey;

  return modifier && (event.code === 'KeyK' || event.key.toLowerCase() === 'k');
}
