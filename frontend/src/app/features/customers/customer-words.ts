import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';

/**
 * What to call a customer, in the reader's language.
 *
 * <strong>A name somebody chose is shown exactly as it was typed.</strong> For a
 * customer with no name the server sends an English label built from how they
 * reach the workspace (`CustomerLabels.For`); this applies the same rule, in
 * the same order, with the words translated -- so an Arabic screen never
 * shows "Anonymous visitor", and which customers are named never changes.
 * Never a name taken from something the customer said.
 */
export function customerName(person: {
  readonly displayName: string | null;
  readonly channels: readonly string[];
}): string {
  if (person.displayName !== null) {
    return person.displayName;
  }

  if (person.channels.length > 1) {
    return $localize`:@@customers.unnamed.several:Unnamed customer`;
  }

  return person.channels.length === 1 && person.channels[0] === 'WhatsApp'
    ? $localize`:@@customers.unnamed.whatsApp:WhatsApp customer`
    : $localize`:@@customers.unnamed.visitor:Anonymous visitor`;
}

/** "1 conversation", "3 conversations", with all six Arabic plural forms. */
export function conversationsPhrase(language: AppLanguage, count: number): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@customers.conversations.zero:No conversations`,
    one: $localize`:@@customers.conversations.one:${n}:count: conversation`,
    two: $localize`:@@customers.conversations.two:${n}:count: conversations`,
    few: $localize`:@@customers.conversations.few:${n}:count: conversations`,
    many: $localize`:@@customers.conversations.many:${n}:count: conversations`,
    other: $localize`:@@customers.conversations.other:${n}:count: conversations`,
  });
}

/**
 * The first letter of a name, for the tile beside it; empty when it has none.
 *
 * A whole grapheme (an accented letter with its mark), never half of one, and
 * a letter rather than whatever character the name starts with: a name that
 * opens with a quote mark or a bracket gets its first letter, never the
 * punctuation. Arabic letters stand alone in their isolated form, which is how
 * a single letter is written.
 */
export function initialOf(name: string): string {
  const letter = /\p{L}/u;

  if (typeof Intl.Segmenter === 'function') {
    for (const { segment } of new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(
      name,
    )) {
      if (letter.test(segment)) {
        return segment.toLocaleUpperCase();
      }
    }

    return '';
  }

  return ([...name].find((character) => letter.test(character)) ?? '').toLocaleUpperCase();
}
