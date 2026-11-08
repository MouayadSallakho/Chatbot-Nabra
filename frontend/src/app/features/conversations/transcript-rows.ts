import type { AppLanguage } from '@app/core/i18n/language';
import { formatDate } from '@app/core/i18n/locale-format';
import { Voice, voiceOf } from '@app/shared/voice/voice';
import type { TranscriptEvent, TranscriptMessage } from './conversations-api';
import { sameDay } from './conversation-words';

/** One line of a rendered transcript. */
export type TranscriptRow =
  | { readonly kind: 'day'; readonly key: string; readonly label: string }
  | { readonly kind: 'event'; readonly key: string; readonly event: TranscriptEvent }
  | {
      readonly kind: 'message';
      readonly key: string;
      readonly message: TranscriptMessage;
      readonly voice: Voice | null;
    };

/** The events this build knows how to word. Any other kind is not drawn. */
const DRAWN_EVENTS: ReadonlySet<string> = new Set(['AiTurnFailed', 'TakenOver', 'ReturnedToAi']);

/**
 * The transcript as rows: messages in ingestion order, with a day separator where
 * the reader's calendar day changes and a divider for what the server says happened.
 *
 * <strong>Every divider is the server's.</strong> Each event is placed after the
 * message whose sequence is its `afterSequence`. Earlier conversations have no
 * events, and nothing is inferred for them from who replied.
 *
 * <strong>Order is the server's.</strong> Rows follow the arrays as given: ingestion
 * order for messages, `mergeEvents` for events. The timestamps only label days.
 */
export function transcriptRows(
  messages: readonly TranscriptMessage[],
  events: readonly TranscriptEvent[],
  language: AppLanguage,
  now: Date = new Date(),
): TranscriptRow[] {
  const rows: TranscriptRow[] = [];
  const drawn = events.filter((event) => DRAWN_EVENTS.has(event.kind));
  const yesterday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
  let lastDay: Date | null = null;
  let next = 0;

  const eventRow = (event: TranscriptEvent): TranscriptRow => ({
    kind: 'event',
    key: `event-${event.id}`,
    event,
  });

  for (const message of messages) {
    // Everything that happened after an earlier message comes before this one.
    while (next < drawn.length && drawn[next].afterSequence < message.sequence) {
      rows.push(eventRow(drawn[next++]));
    }

    const sent = new Date(message.sentAt);

    if (!Number.isNaN(sent.getTime()) && (lastDay === null || !sameDay(lastDay, sent))) {
      rows.push({
        kind: 'day',
        key: `day-${message.id}`,
        label: dayLabel(sent, now, yesterday, language),
      });
      lastDay = sent;
    }

    rows.push({ kind: 'message', key: message.id, message, voice: voiceOf(message.sender) });
  }

  while (next < drawn.length) {
    rows.push(eventRow(drawn[next++]));
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
 * Folds newly read events into what is held, each one once.
 *
 * Keyed on `id`, because an event exactly on a forward page's boundary can come
 * with both pages. In transcript order, oldest first; two events after the same
 * message keep the order they happened in.
 */
export function mergeEvents(
  current: readonly TranscriptEvent[],
  incoming: readonly TranscriptEvent[] | null | undefined,
): readonly TranscriptEvent[] {
  if (!incoming || incoming.length === 0) {
    return current;
  }

  const byId = new Map(current.map((event) => [event.id, event]));

  for (const event of incoming) {
    byId.set(event.id, event);
  }

  return [...byId.values()].sort(
    (left, right) =>
      left.afterSequence - right.afterSequence || Date.parse(left.at) - Date.parse(right.at),
  );
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
