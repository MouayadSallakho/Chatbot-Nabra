import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { CONVERSATION_PROBLEM_WORDS } from './conversation-problems';

describe('CONVERSATION_PROBLEM_WORDS', () => {
  const refusal = (code: string, status = 400) =>
    new HttpErrorResponse({
      status,
      error: { title: 'Refused', detail: 'The English sentence from the server.', code },
    });

  beforeAll(() => registerProblemWords(CONVERSATION_PROBLEM_WORDS));

  it.each([
    ['conversations.filter.invalid', 'This list of conversations could not be shown.'],
    ['conversations.read.sequence.invalid', 'could not be marked as read'],
    ['transcript.cursor.conflict', 'These messages could not be loaded.'],
    ['transcript.include.invalid', 'These messages could not be loaded.'],
    ['whatsapp.conversation.not_on_whatsapp', 'did not come from WhatsApp'],
    ['whatsapp.message.required', 'Write a message'],
    ['whatsapp.message.too_long', 'longer than WhatsApp allows'],
    // Phase 38.7b's codes, worded in 38.8.
    ['conversations.id.invalid', 'This conversation could not be found.'],
    ['conversations.reply.not_taken_over', 'Choose Take over first, then reply.'],
    ['conversations.client_message_id.required', 'This reply could not be sent.'],
    ['conversations.client_message_id.too_long', 'This reply could not be sent.'],
    ['conversations.client_message_id.invalid', 'This reply could not be sent.'],
    ['conversations.message.required', 'Write a reply before sending it.'],
    ['conversations.message.too_long', 'This reply is too long.'],
    ['conversations.message.control_characters', 'Paste it as plain text'],
    ['conversations.assignee.not_member', 'not a member of this workspace'],
    ['conversations.assignee.cannot_reply', 'cannot reply to customers'],
    ['conversations.closed', 'This conversation is closed'],
  ])('words %s by its code, not by the server sentence', (code, words) => {
    const error = toApiError(refusal(code));

    expect(error.code).toBe(code);
    expect(error.message).toContain(words);
    expect(error.message).not.toContain('The English sentence');
  });

  it('gives an unregistered code the generic words, never the server sentence', () => {
    expect(toApiError(refusal('conversations.some.future.code')).message).toBe(
      'Some of the details were rejected. Check the highlighted fields and try again.',
    );
  });
});
