import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/** The same refusal in both cases: nothing a reader did can cause either. */
const TRANSCRIPT_REFUSED = $localize`:@@problem.transcript.refused:These messages could not be loaded. Reload the page and try again.`;

/** A reply whose request id the page itself got wrong (Phase 38.8): nothing the reader typed. */
const REPLY_NOT_SENT = $localize`:@@problem.conversations.reply.notSent:This reply could not be sent. Reload the page and try again. What you wrote is still in the box.`;

/**
 * The inbox's refusals, by the server's stable `code`.
 *
 * Registered by the inbox's routes, so the words arrive with the feature and cost
 * the startup JavaScript nothing. A code that is not here keeps today's generic
 * wording, and the English `detail` is never what an Arabic reader sees.
 */
export const CONVERSATION_PROBLEM_WORDS: ProblemCatalogue = {
  'conversations.filter.invalid': $localize`:@@problem.conversations.filter.invalid:This list of conversations could not be shown. Open the full list instead.`,
  'conversations.read.sequence.invalid': $localize`:@@problem.conversations.read.sequence.invalid:This conversation could not be marked as read. Try again.`,
  'transcript.cursor.conflict': TRANSCRIPT_REFUSED,
  'transcript.include.invalid': TRANSCRIPT_REFUSED,
  'whatsapp.conversation.not_on_whatsapp': $localize`:@@problem.whatsapp.conversation.not_on_whatsapp:This conversation did not come from WhatsApp, so a reply cannot go there.`,
  'whatsapp.message.required': $localize`:@@problem.whatsapp.message.required:Write a message before sending it.`,
  'whatsapp.message.too_long': $localize`:@@problem.whatsapp.message.too_long:This message is longer than WhatsApp allows. Shorten it and send it again.`,

  // Replies, handoff and assignment (Phase 38.7b, worded in 38.8).
  'conversations.id.invalid': $localize`:@@problem.conversations.id.invalid:This conversation could not be found. Open it again from the inbox.`,
  'conversations.reply.not_taken_over': $localize`:@@problem.conversations.reply.not_taken_over:The AI is answering this conversation. Choose Take over first, then reply.`,
  'conversations.client_message_id.required': REPLY_NOT_SENT,
  'conversations.client_message_id.too_long': REPLY_NOT_SENT,
  'conversations.client_message_id.invalid': REPLY_NOT_SENT,
  'conversations.message.required': $localize`:@@problem.conversations.message.required:Write a reply before sending it.`,
  'conversations.message.too_long': $localize`:@@problem.conversations.message.too_long:This reply is too long. Shorten it and send it again.`,
  'conversations.message.control_characters': $localize`:@@problem.conversations.message.control_characters:This reply has characters that cannot be sent. Paste it as plain text and try again.`,
  'conversations.assignee.not_member': $localize`:@@problem.conversations.assignee.not_member:That person is not a member of this workspace. Choose somebody else.`,
  'conversations.assignee.cannot_reply': $localize`:@@problem.conversations.assignee.cannot_reply:That member cannot reply to customers, so the customer would keep waiting. Choose somebody who can reply.`,
  'conversations.closed': $localize`:@@problem.conversations.closed:This conversation is closed, so it cannot be changed.`,
};
