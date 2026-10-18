/**
 * What the reader chose. `system` is a choice in its own right, not the absence
 * of one -- it means "keep following the operating system", which is different
 * from having picked light and happening to be in light.
 */
export type ThemePreference = 'system' | 'light' | 'dark';

/** What is actually painted. `system` has been resolved away by this point. */
export type ResolvedTheme = 'light' | 'dark';

/**
 * The order the three options are offered in.
 *
 * System first, because it is the default and the one most people want; light
 * and dark then read as deliberate overrides rather than as the primary choice.
 */
export const THEME_PREFERENCES: readonly ThemePreference[] = ['system', 'light', 'dark'];

/**
 * Where the preference is kept.
 *
 * DUPLICATED IN src/index.html, which has to read this before the bundle exists
 * in order to avoid a flash of the wrong theme. A unit test reads the HTML file
 * and asserts the two agree, so the copy cannot rot unnoticed.
 */
export const THEME_STORAGE_KEY = 'omniai.theme';

/**
 * The attribute the palette is selected by.
 *
 * Carries the RESOLVED theme, never the preference: `data-theme="system"` would
 * force every themed rule to also handle the media query, which is exactly the
 * duplication the token layer exists to avoid.
 */
export const THEME_ATTRIBUTE = 'data-theme';

/** Human labels for the theme control. */
export const THEME_LABELS: Readonly<Record<ThemePreference, string>> = {
  system: $localize`:@@theme.system:System`,
  light: $localize`:@@theme.light:Light`,
  dark: $localize`:@@theme.dark:Dark`,
};

/** Narrows an unknown stored string to a preference. */
export function isThemePreference(value: unknown): value is ThemePreference {
  return typeof value === 'string' && (THEME_PREFERENCES as readonly string[]).includes(value);
}
