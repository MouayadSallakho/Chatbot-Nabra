import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * A visitor's own message refusals, on the hosted page and in the widget.
 *
 * Everything else a public chat can answer is deliberately uncoded (one generic
 * `404` for an unknown key, a website nobody approved and a withdrawn approval),
 * so a site or a key cannot be enumerated: see `visitor-copy.ts`.
 */
export const CHAT_PROBLEM_WORDS: ProblemCatalogue = {
  'chat.message.required': $localize`:@@problem.chat.message.required:Write a message before sending it.`,
  'chat.message.too_long': $localize`:@@problem.chat.message.too_long:This message is too long. Shorten it and send it again.`,
  'chat.message.in_progress': $localize`:@@problem.chat.message.in_progress:This message is still being answered. Please wait for the reply.`,
  'chat.message.already_failed': $localize`:@@problem.chat.message.already_failed:This message could not be answered. Send a new message to try again.`,
};
