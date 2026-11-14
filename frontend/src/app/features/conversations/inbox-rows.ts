import { channelWord } from '@app/shared/channel/channel-badge';
import type { ChipTone } from '@app/shared/chip/chip';
import type { ConversationSummary, ConversationView, WorkspaceMember } from './conversations-api';
import { needsPersonWord, senderWord } from './conversation-words';

/**
 * A colleague's name as the inbox shows it (Phase 39.1): the display name they
 * chose, or their email where they have none. `isEmail` lets the template
 * isolate an address left to right and a name in its own direction.
 */
export interface MemberName {
  readonly text: string;
  readonly isEmail: boolean;
}

export function memberName(member: WorkspaceMember): MemberName {
  const name = member.displayName?.trim();

  return name ? { text: name, isEmail: false } : { text: member.email, isEmail: true };
}

/** Why a row waits for a person, in words, and the chip's hue. */
export interface RowReason {
  readonly word: string;
  /** Coral when the AI could not reply; gold for every other wait (calm-glass.md §6). */
  readonly tone: Extract<ChipTone, 'failed' | 'need'>;
}

/**
 * Who has the conversation, said once (Phase 38 note 13): who handles it when
 * somebody took it over, otherwise who it is assigned to.
 */
export interface RowOwner {
  readonly kind: 'handled' | 'assigned';
  readonly name: MemberName;
}

/** One row of the list, with what its template shows worked out once per read. */
export interface InboxRow {
  readonly conversation: ConversationSummary;
  /** Who wrote the last message, as a word ("Customer", "AI"…), or empty. */
  readonly sender: string;
  /** The channel in words: the avatar's badge is a glyph, hidden from assistive technology. */
  readonly channel: string;
  readonly reason: RowReason | null;
  readonly owner: RowOwner | null;
  /**
   * The voice chip, when nothing else says who answers: "AI is answering" while
   * the AI has it and nothing is wrong, "Person" for a person's conversation
   * whose taker the API does not know (a takeover from before Phase 37.4).
   */
  readonly voice: 'ai' | 'human' | null;
}

/**
 * Works out a row's chips from what the server said, and nothing else.
 *
 * - The reason chip comes from the server's `needsPerson` and its reason (38.7a,
 *   BA-7), in every view; in the needs-a-person view every row has one. A
 *   summary from a server older than that falls back to what a row could always
 *   infer: a person has it and the customer wrote last.
 * - One "who has it" chip (note 13).
 * - "AI is answering" only while the AI has it and nothing is waiting: beside
 *   "The AI could not reply" it would contradict the row.
 */
export function inboxRow(conversation: ConversationSummary, view: ConversationView): InboxRow {
  const human = conversation.handling === 'Human';
  // In the needs-a-person view every row waits, by the filter's own definition.
  const waits =
    view === 'needs-person' ||
    (conversation.needsPerson ?? (human && conversation.lastMessageSender === 'Customer'));
  const reasonCode = waits ? (conversation.needsPersonReason ?? null) : null;

  const reason: RowReason | null = waits
    ? {
        word: needsPersonWord(reasonCode),
        tone: reasonCode === 'AiTurnFailed' ? 'failed' : 'need',
      }
    : null;

  const owner: RowOwner | null =
    human && conversation.takenOverBy
      ? { kind: 'handled', name: memberName(conversation.takenOverBy) }
      : conversation.assignedTo
        ? { kind: 'assigned', name: memberName(conversation.assignedTo) }
        : null;

  const voice = human ? (owner === null ? 'human' : null) : reason === null ? 'ai' : null;

  return {
    conversation,
    sender: senderWord(conversation.lastMessageSender),
    channel: channelWord(conversation.channel),
    reason,
    owner,
    voice,
  };
}
