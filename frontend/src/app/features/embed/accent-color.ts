/**
 * The two foregrounds the widget will put on an accent, and nothing between.
 *
 * Near-black rather than pure black, because pure black on a saturated colour
 * reads as a hole; near-white for the same reason in reverse.
 */
const DARK_FOREGROUND = '#111827';
const LIGHT_FOREGROUND = '#ffffff';

/**
 * The contrast threshold, from WCAG's own definition.
 *
 * A ratio of 4.5:1 is the AA requirement for body text. Comparing the two
 * candidates and taking the better one guarantees the launcher's label is legible
 * on every colour a workspace can choose — including the awkward middle greens
 * and oranges where a fixed white label quietly fails.
 */
function contrast(a: number, b: number): number {
  const lighter = Math.max(a, b);
  const darker = Math.min(a, b);

  return (lighter + 0.05) / (darker + 0.05);
}

/**
 * A text colour that can actually be read on the given accent.
 *
 * <strong>Computed rather than configured.</strong> A workspace picks one colour;
 * asking them to also pick a legible foreground for it would be asking them to do
 * the contrast arithmetic, and the answer would be wrong often enough to matter.
 *
 * Input that is not a `#rrggbb` colour falls back to white. The server only ever
 * stores the expanded, lower-cased six-digit form, so this is defence against a
 * response that could not have come from it rather than a case in normal use.
 */
export function readableForeground(accent: string): string {
  const luminance = relativeLuminance(accent);

  if (luminance === null) {
    return LIGHT_FOREGROUND;
  }

  const onLight = contrast(luminance, relativeLuminance(LIGHT_FOREGROUND) ?? 1);
  const onDark = contrast(luminance, relativeLuminance(DARK_FOREGROUND) ?? 0);

  return onDark > onLight ? DARK_FOREGROUND : LIGHT_FOREGROUND;
}

/**
 * WCAG relative luminance, or `null` when the value is not a hex colour.
 *
 * The channel transform is the standard one: sRGB values are gamma-encoded, so
 * averaging the raw bytes — the obvious shortcut — gets mid-tones visibly wrong.
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
