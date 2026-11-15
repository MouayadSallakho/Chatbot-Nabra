import { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import type { ProblemCatalogue, ProblemFacts } from '@app/core/problems/problem-words';

/**
 * The longest display name the server keeps (Phase 39.1): 80 characters after
 * trimming, counted in UTF-16 code units, which is what a string's `length` is.
 */
export const DISPLAY_NAME_LIMIT = 80;

/** "Your display name can be at most 80 characters.", with all six Arabic forms. */
export function displayNameTooLong(language: AppLanguage, limit = DISPLAY_NAME_LIMIT): string {
  const n = formatNumber(language, limit);

  return pluralMessage(language, limit, {
    zero: $localize`:@@problem.account.display_name.too_long.zero:Your display name can be at most ${n}:limit: characters. Shorten it and save again.`,
    one: $localize`:@@problem.account.display_name.too_long.one:Your display name can be at most ${n}:limit: character. Shorten it and save again.`,
    two: $localize`:@@problem.account.display_name.too_long.two:Your display name can be at most ${n}:limit: characters. Shorten it and save again.`,
    few: $localize`:@@problem.account.display_name.too_long.few:Your display name can be at most ${n}:limit: characters. Shorten it and save again.`,
    many: $localize`:@@problem.account.display_name.too_long.many:Your display name can be at most ${n}:limit: characters. Shorten it and save again.`,
    other: $localize`:@@problem.account.display_name.too_long.other:Your display name can be at most ${n}:limit: characters. Shorten it and save again.`,
  });
}

/** For a name with a control character or a direction mark in it. */
export const DISPLAY_NAME_CONTROL_CHARACTERS = $localize`:@@problem.account.display_name.control_characters:Your display name contains a hidden character, such as a direction mark. Remove it and save again.`;

/**
 * The display name's refusals, by the server's `code` (problem-codes.md).
 *
 * Registered by the dialog when it loads, so they cost the startup JavaScript
 * nothing. A code this build does not know keeps the generic wording.
 */
export const DISPLAY_NAME_PROBLEMS: ProblemCatalogue = {
  'account.display_name.too_long': ({ language, limit }: ProblemFacts) =>
    displayNameTooLong(language, limit ?? DISPLAY_NAME_LIMIT),
  'account.display_name.control_characters': DISPLAY_NAME_CONTROL_CHARACTERS,
};

/**
 * The name as the server will keep it: trimmed, inner runs of white space made
 * one space, and empty meaning "no name" (null). The same rules as
 * `MemberDisplayName`, so the screen can show the saved value without asking.
 */
export function normaliseDisplayName(value: string): string | null {
  const name = value.trim().replace(/\s+/gu, ' ');

  return name === '' ? null : name;
}

/**
 * Whether the name holds a character the server refuses: a control character,
 * or a format character other than the two joiners Arabic and emoji need
 * (U+200C ZWNJ, U+200D ZWJ).
 */
export function hasControlCharacters(value: string): boolean {
  return /[\p{Cc}\p{Cf}]/u.test(value.replace(/[‌‍]/gu, ''));
}
