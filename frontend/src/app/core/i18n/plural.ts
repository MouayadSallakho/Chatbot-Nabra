import { AppLanguage } from './language';

/** The six CLDR plural categories. English uses two; Arabic uses all six. */
export type PluralCategory = 'zero' | 'one' | 'two' | 'few' | 'many' | 'other';

/**
 * One full sentence per category.
 *
 * All six are required, in English too, because a translation can only target a
 * message the source declares: Arabic needs its own `two` ("عميلان"), `few` (3-10,
 * "عملاء") and `many` (11-99, "عميلًا") sentences, so the English source carries
 * those IDs with its `other` wording. Full sentences, never a number glued to a
 * noun: Arabic word order and agreement change with the count.
 */
export type PluralForms = Readonly<Record<PluralCategory, string>>;

const rules = new Map<AppLanguage, Intl.PluralRules>();

export function pluralCategory(language: AppLanguage, count: number): PluralCategory {
  let rule = rules.get(language);

  if (!rule) {
    rule = new Intl.PluralRules(language);
    rules.set(language, rule);
  }

  return rule.select(count) as PluralCategory;
}

/**
 * Picks the sentence for a count, for text that has to be built in code (a
 * document title, a toast, a live-region announcement). Templates use ICU
 * `plural` instead, which Angular resolves with the same CLDR rules.
 */
export function pluralMessage(language: AppLanguage, count: number, forms: PluralForms): string {
  return forms[pluralCategory(language, count)];
}
