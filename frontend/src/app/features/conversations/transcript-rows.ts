import type { AppLanguage } from '@app/core/i18n/language';
import { formatDate } from '@app/core/i18n/locale-format';
import { Voice, voiceOf } from '@app/shared/voice/voice';
import type { TranscriptMessage } from './conversations-api';
import { sameDay } from './conversation-words';

/** One line of a rendered transcript. */
export type TranscriptRow =
  | { readonly kind: 'day'; readonly key: string; readonly label: string }
  | { readonly kind: 'handoff'; readonly key: string; readonly to: Voice }
  | {
      readonly kind: 'message';
      readonly key: string;
      readonly message: TranscriptMessage;
      readonly voice: Voice | null;
    };

/**
 * The transcript as rows: messages in ingestion order, with a day separator
 * where the reader's calendar day changes and a handoff divider where the
 * replying voice changes.
 *
 * <strong>Every divider comes from the messages themselves.</strong> A divider
 * goes before a reply whose voice (AI or person) differs from the previous
 * reply's -- the same rule the voice track and the Design Lab use. A person
 * can only reply after taking a conversation over, and the AI only answers
 * once it has it back, so an author change is a handoff that really happened.
 * A takeover with no reply after it yet draws nothing: the API has no handoff
 * events, and the transcript does not invent one.
 *
 * <strong>Order is the server's.</strong> Rows follow the array as given
 * (ingestion order); the timestamps only label days, they never sort.
 */
export function transcriptRows(
  messages: readonly TranscriptMessage[],
  language: AppLanguage,
  now: Date = new Date(),
): TranscriptRow[] {
  const rows: TranscriptRow[] = [];
  const yesterday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
  let lastDay: Date | null = null;
  let lastReplyVoice: Voice | null = null;

  for (const message of messages) {
    const sent = new Date(message.sentAt);

    if (!Number.isNaN(sent.getTime()) && (lastDay === null || !sameDay(lastDay, sent))) {
      rows.push({
        kind: 'day',
        key: `day-${message.id}`,
        label: dayLabel(sent, now, yesterday, language),
      });
      lastDay = sent;
    }

    const voice = voiceOf(message.sender);

    if (voice !== null) {
      if (lastReplyVoice !== null && voice !== lastReplyVoice) {
        rows.push({ kind: 'handoff', key: `handoff-${message.id}`, to: voice });
      }

      lastReplyVoice = voice;
    }

    rows.push({ kind: 'message', key: message.id, message, voice });
  }

  return rows;
}

function dayLabel(day: Date, now: Date, yesterday: Date, language: AppLanguage): string {
  if (sameDay(day, now)) {
    return $localize`:@@transcript.day.today:Today`;
  }

  if (sameDay(day, yesterday)) {
    return $localize`:@@transcript.day.yesterday:Yesterday`;
  }

  return formatDate(language, day, 'longDate');
}

/**
 * Folds newly read messages into what is on screen, without duplicating.
 *
 * Keyed on the ingestion sequence, which is the platform's own ordering and is
 * unique within a conversation. Sorting by it afterwards is what puts a message
 * that committed late in its right place rather than at the bottom.
 */
export function mergeMessages(
  current: readonly TranscriptMessage[],
  incoming: readonly TranscriptMessage[],
): readonly TranscriptMessage[] {
  const bySequence = new Map(current.map((message) => [message.sequence, message]));

  for (const message of incoming) {
    bySequence.set(message.sequence, message);
  }

  return [...bySequence.values()].sort((left, right) => left.sequence - right.sequence);
}

/**
 * Where a re-read should continue from.
 *
 * Not the last sequence on screen but one a few messages back. Sequences are
 * handed out when a message is inserted and become visible when its
 * transaction commits, so a message can appear after one with a higher
 * sequence. Re-reading a short overlap and merging by sequence catches it;
 * the overlap costs a few rows, never a duplicate.
 */
export function refreshCursor(
  messages: readonly TranscriptMessage[],
  overlap = REFRESH_OVERLAP,
): number | null {
  if (messages.length <= overlap) {
    return null;
  }

  return messages[messages.length - 1 - overlap].sequence;
}

/** How many already-loaded messages a re-read reads again. */
export const REFRESH_OVERLAP = 20;
