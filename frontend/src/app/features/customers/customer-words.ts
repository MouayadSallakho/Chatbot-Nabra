import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import type { ConversationCustomer } from '@app/features/conversations/conversations-api';

/**
 * A directory record in the shape the inbox names customers by (Phase 38.8, BA-1).
 *
 * Since 38.7a the customer reads carry the inbox's `labelCode` and `shortCode`, so
 * the customers screens use the inbox's label helper (`customer-label.ts`) and no
 * longer work the rule out from channels. The directory sends no masked WhatsApp
 * number, so an unnamed WhatsApp customer is told apart by the short code here.
 */
export function asLabelled(person: {
  readonly id: string;
  readonly displayName: string | null;
  readonly labelCode: string;
  readonly shortCode: string;
}): ConversationCustomer {
  return {
    id: person.id,
    displayName: person.displayName,
    labelCode: person.labelCode,
    reference: null,
    shortCode: person.shortCode,
  };
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
