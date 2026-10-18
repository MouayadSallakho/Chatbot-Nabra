/**
 * The product's public name: Nabra in English, نبرة in Arabic.
 *
 * نبرة means "tone of voice" -- two voices, the AI and the team, speaking in
 * one tone (docs/design/MASTER.md, "Name"). It is the only name a person using
 * the product ever sees or hears.
 *
 * OmniAI is the internal code name: the repository, namespaces, storage keys,
 * protocol strings, headers, the database and Docker. It never reaches a screen,
 * a tab title or an accessible name.
 *
 * The same message ID marks the wordmark in templates (`i18n="@@brand.name"`),
 * so the English and Arabic forms live in one place in the catalogues.
 * Evaluated at import, which is after main.ts has loaded the Arabic catalogue.
 */
export const PRODUCT_NAME = $localize`:@@brand.name:Nabra`;
