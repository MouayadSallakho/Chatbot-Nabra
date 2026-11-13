import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { CUSTOMER_PROBLEM_WORDS } from './customer-problems';

/** Every code the customers screens can meet; the Arabic is proved in `arabic-render.spec.ts`. */
describe('CUSTOMER_PROBLEM_WORDS', () => {
  const refusal = (code: string, status = 400) =>
    new HttpErrorResponse({
      status,
      error: { title: 'Refused', detail: 'The English sentence from the server.', code },
    });

  beforeAll(() => registerProblemWords(CUSTOMER_PROBLEM_WORDS));

  it('covers every customers code in problem-codes.md (Phase 38.7b)', () => {
    expect(Object.keys(CUSTOMER_PROBLEM_WORDS).sort()).toEqual(
      [
        'customers.page.invalid',
        'customers.id.invalid',
        'customers.display_name.too_long',
        'customers.display_name.control_characters',
        'customers.merge.target_required',
        'customers.merge.self',
        'customers.merge.not_in_workspace',
        'customers.merged',
      ].sort(),
    );
  });

  it.each([
    ['customers.page.invalid', 400, 'Go back to the first page.'],
    ['customers.id.invalid', 400, 'This customer could not be found.'],
    ['customers.display_name.too_long', 400, 'This name is too long.'],
    ['customers.display_name.control_characters', 400, 'one line of plain text'],
    ['customers.merge.target_required', 400, 'Choose the customer to combine with first.'],
    ['customers.merge.self', 400, 'cannot be combined with itself'],
    ['customers.merge.not_in_workspace', 400, 'no longer in this workspace'],
    ['customers.merged', 409, 'already been combined with another one'],
  ])('words %s by its code, not by the server sentence', (code, status, words) => {
    const error = toApiError(refusal(code, status));

    expect(error.code).toBe(code);
    expect(error.message).toContain(words);
    expect(error.message).not.toContain('The English sentence');
  });

  it('gives an unregistered code the generic words, never the server sentence', () => {
    expect(toApiError(refusal('customers.some.future.code')).message).toBe(
      'Some of the details were rejected. Check the highlighted fields and try again.',
    );
  });
});
