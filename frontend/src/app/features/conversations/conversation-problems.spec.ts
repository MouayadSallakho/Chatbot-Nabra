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
  ])('words %s by its code, not by the server sentence', (code, words) => {
    const error = toApiError(refusal(code));

    expect(error.code).toBe(code);
    expect(error.message).toContain(words);
    expect(error.message).not.toContain('The English sentence');
  });

  it('keeps an unregistered code on the wording it has today', () => {
    expect(toApiError(refusal('conversations.some.future.code')).message).toBe(
      'The English sentence from the server.',
    );
  });
});
