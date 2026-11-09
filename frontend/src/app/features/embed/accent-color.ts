/**
 * The accent colour rules, in one place (Phase 36.9).
 *
 * A workspace picks one colour for its website chat. Everything else the widget
 * draws on or around that colour is derived here, and checked against WCAG 2
 * contrast ratios before it is applied: the label on the accent, and whether
 * the closed launcher is still visible on an ordinary white page. The widget,
 * the hosted page's preview and the settings screen all call these functions, so
 * a colour cannot pass one check and fail another.
 *
 * <strong>Nothing here can refuse a colour.</strong> The server stores any
 * `#rrggbb`; the rules make whatever was stored legible instead, and the
 * settings screen says what they did and why.
 */

/** The widget's own accent, until (or unless) a workspace chooses one. */
export const DEFAULT_ACCENT = '#2563eb'; // token-lint-ignore: the server's default accent

/**
 * The foregrounds the widget will put on an accent, and nothing between.
 *
 * Near-black rather than pure black, because pure black on a saturated colour
 * reads as a hole; near-white for the same reason in reverse. Pure black is the
 * last resort for the narrow band of mid-tones where neither reaches 4.5:1.
 */
// token-lint-ignore-next-line: the rule's fixed candidates, not theme colours
const DARK_FOREGROUND = '#111827'; // token-lint-ignore
const LIGHT_FOREGROUND = '#ffffff'; // token-lint-ignore
const LAST_RESORT_FOREGROUND = '#000000'; // token-lint-ignore

/** What the host page most often is, and the dark page the checks also try. */
const LIGHT_PAGE = '#ffffff'; // token-lint-ignore: a white host page
const DARK_PAGE = '#0f1012'; // token-lint-ignore: a dark host page (the night-shift page colour)

/** WCAG 2: text (1.4.3) and the boundaries of controls (1.4.11). */
export const TEXT_CONTRAST = 4.5;
export const BOUNDARY_CONTRAST = 3;

/** Everything the rules decided about one accent. */
export interface AccentReport {
  /** The accent actually used: the input, or the default when it was not a colour. */
  readonly accent: string;
  /** The text colour on the accent. */
  readonly foreground: string;
  /** The label's contrast on the accent: at least 4.5:1 by construction. */
  readonly labelContrast: number;
  /** The accent against a white page, and against a dark one. */
  readonly onLightPage: number;
  readonly onDarkPage: number;
  /**
   * The outline drawn around the launcher, or null when it needs none.
   *
   * A pale accent on a white page is a launcher nobody can find, so it gets a
   * 1px edge in its own label colour, which is dark exactly when the accent is
   * light. The host page's real colour cannot be known from inside the frame;
   * white is the common case and the one this guards.
   */
  readonly edge: string | null;
}

/** Whether a value is the `#rrggbb` form the server stores. */
export function isAccent(value: string): boolean {
  return /^#[0-9a-f]{6}$/i.test(value.trim());
}

/** The contrast between two `#rrggbb` colours, or null if either is not one. */
export function contrastRatio(a: string, b: string): number | null {
  const first = relativeLuminance(a);
  const second = relativeLuminance(b);

  if (first === null || second === null) {
    return null;
  }

  const lighter = Math.max(first, second);
  const darker = Math.min(first, second);

  return (lighter + 0.05) / (darker + 0.05);
}

/**
 * A text colour that can actually be read on the given accent.
 *
 * <strong>Computed rather than configured.</strong> Asking a workspace to also
 * pick a legible foreground would be asking them to do the contrast arithmetic.
 * The better of near-black and white is taken; when even that is under 4.5:1 (a
 * band of mid-tones around 20% luminance), pure black, which always clears it
 * there.
 *
 * Input that is not a `#rrggbb` colour falls back to white. The server only ever
 * stores the expanded six-digit form, so this is defence against a response that
 * could not have come from it rather than a case in normal use.
 */
export function readableForeground(accent: string): string {
  if (!isAccent(accent)) {
    return LIGHT_FOREGROUND;
  }

  const onLight = contrastRatio(accent, LIGHT_FOREGROUND) ?? 0;
  const onDark = contrastRatio(accent, DARK_FOREGROUND) ?? 0;
  const best = onDark > onLight ? DARK_FOREGROUND : LIGHT_FOREGROUND;

  return Math.max(onLight, onDark) >= TEXT_CONTRAST ? best : LAST_RESORT_FOREGROUND;
}

/**
 * Checks an accent before anything is painted with it.
 *
 * Anything that is not a colour is replaced by the default first, so a value
 * that could not have come from the server can never reach a style property.
 */
export function checkAccent(value: string): AccentReport {
  const accent = isAccent(value) ? value.trim().toLowerCase() : DEFAULT_ACCENT;
  const foreground = readableForeground(accent);
  const onLightPage = contrastRatio(accent, LIGHT_PAGE) ?? 0;

  return {
    accent,
    foreground,
    labelContrast: contrastRatio(accent, foreground) ?? 0,
    onLightPage,
    onDarkPage: contrastRatio(accent, DARK_PAGE) ?? 0,
    edge: onLightPage < BOUNDARY_CONTRAST ? foreground : null,
  };
}

/** An accent the hosted chat page may wear, with the text colour that reads on it. */
export interface PageAccent {
  readonly accent: string;
  readonly foreground: string;
}

/**
 * The accent for the hosted chat page, or null for the product's own look.
 *
 * Stricter than the widget's rules, on purpose (Phase 38.3). Only an exact
 * `#RRGGBB` is accepted, with nothing trimmed, because this value comes from a
 * public read and reaches a style. The text is the lighter or the darker ink,
 * whichever reaches 4.5:1; where neither does, the page keeps Nabra's own look
 * rather than a label that is merely the least bad. Anything else is ignored
 * silently: a visitor has nothing to do about a workspace's colour.
 */
export function pageAccent(value: unknown): PageAccent | null {
  if (typeof value !== 'string' || !/^#[0-9A-Fa-f]{6}$/.test(value)) {
    return null;
  }

  const onLight = contrastRatio(value, LIGHT_FOREGROUND) ?? 0;
  const onDark = contrastRatio(value, DARK_FOREGROUND) ?? 0;

  if (Math.max(onLight, onDark) < TEXT_CONTRAST) {
    return null;
  }

  return {
    accent: value.toLowerCase(),
    foreground: onDark > onLight ? DARK_FOREGROUND : LIGHT_FOREGROUND,
  };
}

/**
 * Paints an accent onto an element as custom properties, after checking it.
 *
 * `setProperty` with an already-validated `#rrggbb` cannot introduce a
 * declaration of its own: a tenant chooses a colour, not CSS.
 */
export function applyAccent(element: HTMLElement, value: string): AccentReport {
  const report = checkAccent(value);

  element.style.setProperty('--chat-accent', report.accent);
  element.style.setProperty('--chat-on-accent', report.foreground);

  if (report.edge) {
    element.style.setProperty('--chat-accent-edge', report.edge);
  } else {
    element.style.removeProperty('--chat-accent-edge');
  }

  return report;
}

/**
 * WCAG relative luminance, or `null` when the value is not a hex colour.
 *
 * The channel transform is the standard one: sRGB values are gamma-encoded, so
 * averaging the raw bytes (the obvious shortcut) gets mid-tones visibly wrong.
 */
function relativeLuminance(colour: string): number | null {
  const match = /^#([0-9a-f]{6})$/i.exec(colour.trim());

  if (!match) {
    return null;
  }

  const value = Number.parseInt(match[1], 16);
  const channels = [(value >> 16) & 0xff, (value >> 8) & 0xff, value & 0xff].map((byte) => {
    const channel = byte / 255;

    return channel <= 0.03928 ? channel / 12.92 : Math.pow((channel + 0.055) / 1.055, 2.4);
  });

  return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
}
