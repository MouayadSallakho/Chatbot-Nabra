/**
 * One or two letters for a member (Phase 39.3, shared since 39.6a): the first
 * letters of the first two words of the display name they chose ("Sara Ahmed"
 * gives SA; an Arabic name gives Arabic letters), or, without one, of the
 * address's local part ("p36.owner@…" gives PO). Never a guess at a real name.
 *
 * Its own file, so the account menu (in the startup JavaScript) can use it
 * without pulling in the avatar component.
 */
export function memberInitials(displayName: string | null | undefined, email: string): string {
  const display = displayName?.trim();

  if (display) {
    const words = display.split(/\s+/).filter(Boolean);
    const letters = words.length > 1 ? [...words[0]][0] + [...words[1]][0] : [...words[0]][0];

    return letters.toLocaleUpperCase();
  }

  const local = email.split('@')[0] ?? '';
  const parts = local.split(/[._-]+/).filter(Boolean);
  const letters = parts.length > 1 ? parts[0][0] + parts[1][0] : local.slice(0, 1);

  return letters.toUpperCase() || '?';
}

/** The name a member is shown by: the display name they chose, else their address. */
export function memberShownName(displayName: string | null | undefined, email: string): string {
  return displayName?.trim() || email;
}
