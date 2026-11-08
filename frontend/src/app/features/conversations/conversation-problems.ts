import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/** The same refusal in both cases: nothing a reader did can cause either. */
const TRANSCRIPT_REFUSED = $localize`:@@problem.transcript.refused:These messages could not be loaded. Reload the page and try again.`;

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
};
