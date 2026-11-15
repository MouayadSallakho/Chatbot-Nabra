import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { countMessage } from '@app/core/i18n/plural';
import type { ConversationCustomer } from '@app/features/conversations/conversations-api';
import { customerLabel, customerLabelPlain } from '@app/features/conversations/customer-label';
import { codeInitials } from '@app/shared/avatar/avatar';

/**
 * A directory record in the shape the inbox names customers by (Phase 38.8, BA-1).
 *
 * Since 38.7a the customer reads carry the inbox's `labelCode` and `shortCode`, so
 * the customers screens use the inbox's label helper (`customer-label.ts`) and no
 * longer work the rule out from channels. Since 39.1 they also carry `reference`,
 * the masked WhatsApp number (note 18), so an unnamed WhatsApp customer reads
 * "WhatsApp customer ••••1234" here exactly as in the inbox. A read without it
 * (an older server) falls back to the short code, as before.
 */
export function asLabelled(person: {
  readonly id: string;
  readonly displayName: string | null;
  readonly labelCode: string;
  readonly shortCode: string;
  readonly reference?: string | null;
}): ConversationCustomer {
  return {
    id: person.id,
    displayName: person.displayName,
    labelCode: person.labelCode,
    reference: person.reference ?? null,
    shortCode: person.shortCode,
  };
}

/**
 * The avatar's letters and badge for a customer, as the inbox draws them: a
 * name's initials, or the first two characters of the code its label shows (the
 * short code, or the masked number's digits), with the badge of the channel
 * they first reached the workspace on.
 */
export function customerAvatar(
  label: ConversationCustomer,
  channels: readonly string[],
): { readonly name: string; readonly text: string; readonly channel: string } {
  const said = customerLabel(label);

  return {
    name: customerLabelPlain(label),
    text: said.kind === 'label' && said.code !== null ? codeInitials(said.code) : '',
    channel: channels[0] ?? '',
  };
}

/**
 * "No conversations", "1 conversation", "3 conversations", with all six Arabic
 * plural forms. None has its own sentence in both languages (`countMessage`):
 * English has no zero category, so it said "0 conversations" (seen on a combined
 * record in Phase 39.6a).
 */
export function conversationsPhrase(language: AppLanguage, count: number): string {
  const n = formatNumber(language, count);

  return countMessage(language, count, {
    zero: $localize`:@@customers.conversations.zero:No conversations`,
    one: $localize`:@@customers.conversations.one:${n}:count: conversation`,
    two: $localize`:@@customers.conversations.two:${n}:count: conversations`,
    few: $localize`:@@customers.conversations.few:${n}:count: conversations`,
    many: $localize`:@@customers.conversations.many:${n}:count: conversations`,
    other: $localize`:@@customers.conversations.other:${n}:count: conversations`,
  });
}
