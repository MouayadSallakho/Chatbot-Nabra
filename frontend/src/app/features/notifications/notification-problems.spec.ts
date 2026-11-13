import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { NOTIFICATION_PROBLEM_WORDS } from './notification-problems';

/** Both notification codes; the Arabic is proved in `arabic-render.spec.ts`. */
describe('NOTIFICATION_PROBLEM_WORDS', () => {
  const refusal = (code: string) =>
    new HttpErrorResponse({
      status: 400,
      error: { title: 'Refused', detail: 'The English sentence from the server.', code },
    });

  beforeAll(() => registerProblemWords(NOTIFICATION_PROBLEM_WORDS));

  it('covers both notifications codes in problem-codes.md (Phase 38.7b)', () => {
    expect(Object.keys(NOTIFICATION_PROBLEM_WORDS).sort()).toEqual([
      'notifications.page.invalid',
      'notifications.preferences.incomplete',
    ]);
  });

  it.each([
    ['notifications.page.invalid', 'This page of notifications could not be shown.'],
    ['notifications.preferences.incomplete', 'Your choices are still selected.'],
  ])('words %s by its code, not by the server sentence', (code, words) => {
    const error = toApiError(refusal(code));

    expect(error.message).toContain(words);
    expect(error.message).not.toContain('The English sentence');
  });

  it('gives an unregistered code the generic words, never the server sentence', () => {
    expect(toApiError(refusal('notifications.some.future.code')).message).toBe(
      'Some of the details were rejected. Check the highlighted fields and try again.',
    );
  });
});
