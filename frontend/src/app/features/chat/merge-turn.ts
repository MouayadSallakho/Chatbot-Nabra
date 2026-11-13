import type { ChatMessage, ChatTurn } from './hosted-chat-api';

/**
 * Adds what one send produced to the visitor's transcript, once (Phase 38.8).
 *
 * <strong>Why by id, and not by appending.</strong> Since Phase 38.7a the
 * server announces a customer's message as soon as it is stored, before the AI
 * answers. The page re-reads on that announcement, so the message is usually
 * already in the list when the send itself answers, and the settled
 * announcement can even bring the reply first. Appending would show either of
 * them twice; this keeps every message exactly once, so the polite live region
 * announces it once.
 *
 * <strong>The order stays the server's.</strong> A message already in the list
 * keeps the place the transcript read gave it (the read is in sequence order).
 * What is missing is added after it, the customer's message before the reply,
 * which is their order in the transcript. The settled announcement re-reads the
 * whole transcript anyway, and that read replaces the list.
 *
 * Returns the same array when nothing is new, so a replayed answer changes
 * nothing on screen.
 */
export function mergeTurn(current: readonly ChatMessage[], turn: ChatTurn): readonly ChatMessage[] {
  const known = new Set(current.map((message) => message.id));
  const arriving =
    turn.reply === null ? [turn.customerMessage] : [turn.customerMessage, turn.reply];
  const fresh = arriving.filter((message) => !known.has(message.id));

  return fresh.length === 0 ? current : [...current, ...fresh];
}
