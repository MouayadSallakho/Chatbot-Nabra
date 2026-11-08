import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import type { ConversationHandling, MessageSender } from './conversations-api';

/**
 * The inbox's words, written once (Phase 36.6).
 *
 * The list, the transcript and the details panel all name the same few things
 * -- who wrote a message, who is answering, whether a conversation is open --
 * and a reader should meet the same word for each wherever it appears, in
 * English and in Arabic (glossary: `src/locale/GLOSSARY.md`).
 */

/** Who wrote the last message, beside a preview in the list. */
export function senderWord(sender: MessageSender | null): string {
  switch (sender) {
    case 'Customer':
      return $localize`:@@inbox.sender.customer:Customer`;
    case 'AiAgent':
      return $localize`:@@inbox.sender.ai:AI`;
    case 'HumanAgent':
      return $localize`:@@inbox.sender.team:Team`;
    case 'System':
      return $localize`:@@inbox.sender.system:Nabra`;
    default:
      return '';
  }
}

/** A sentence for who is answering right now. */
export function handlingSentence(handling: ConversationHandling): string {
  return handling === 'Human'
    ? $localize`:@@conversation.handling.human:A person is answering`
    : $localize`:@@conversation.handling.ai:The AI is answering`;
}

/** Open or closed, as a word. Unknown values are shown as sent. */
export function statusWord(status: string): string {
  switch (status) {
    case 'Open':
      return $localize`:@@conversation.status.open:Open`;
    case 'Closed':
      return $localize`:@@conversation.status.closed:Closed`;
    default:
      return status;
  }
}

/** "1 message", "230 messages", with all six Arabic plural forms. */
export function messagesPhrase(language: AppLanguage, count: number): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@conversation.messages.zero:No messages`,
    one: $localize`:@@conversation.messages.one:${n}:count: message`,
    two: $localize`:@@conversation.messages.two:${n}:count: messages`,
    few: $localize`:@@conversation.messages.few:${n}:count: messages`,
    many: $localize`:@@conversation.messages.many:${n}:count: messages`,
    other: $localize`:@@conversation.messages.other:${n}:count: messages`,
  });
}

/**
 * Whether two instants fall on the same calendar day for this reader.
 *
 * The browser's own time zone: a day separator in a transcript is about the
 * reader's day, and the time printed beside each message is in it too.
 */
export function sameDay(left: Date, right: Date): boolean {
  return (
    left.getFullYear() === right.getFullYear() &&
    left.getMonth() === right.getMonth() &&
    left.getDate() === right.getDate()
  );
}

/** A WhatsApp reply's delivery state, as a word. Unknown values are shown as sent. */
export function deliveryWord(status: string): string {
  switch (status) {
    case 'Pending':
      return $localize`:@@transcript.delivery.pending:Waiting to send`;
    case 'Sending':
      return $localize`:@@transcript.delivery.sending:Sending`;
    case 'Sent':
      return $localize`:@@transcript.delivery.sent:Sent`;
    case 'Delivered':
      return $localize`:@@transcript.delivery.delivered:Delivered`;
    case 'Read':
      return $localize`:@@transcript.delivery.read:Read`;
    case 'Failed':
      return $localize`:@@transcript.delivery.failedWord:Not delivered`;
    case 'Unknown':
      return $localize`:@@transcript.delivery.unknownWord:Delivery unknown`;
    default:
      return status;
  }
}
