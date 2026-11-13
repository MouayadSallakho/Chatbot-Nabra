import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { KNOWLEDGE_PROBLEM_WORDS } from './knowledge-problems';

/** Every code the screens can meet; the Arabic is proved in `arabic-render.spec.ts`. */
describe('KNOWLEDGE_PROBLEM_WORDS', () => {
  const refusal = (code: string, status: number, limit?: number) =>
    new HttpErrorResponse({
      status,
      error: { title: 'Refused', detail: 'The English sentence from the server.', code, limit },
    });

  beforeAll(() => registerProblemWords(KNOWLEDGE_PROBLEM_WORDS));

  it('covers every knowledge code the plan lists', () => {
    expect(Object.keys(KNOWLEDGE_PROBLEM_WORDS).sort()).toEqual(
      [
        'knowledge.file.multipart_required',
        'knowledge.file.required',
        'knowledge.file.too_large',
        'knowledge.file.unsupported_type',
        'knowledge.file.type_mismatch',
        'knowledge.file.empty',
        'knowledge.file.not_utf8',
        'knowledge.file.damaged',
        'knowledge.file.encrypted',
        'knowledge.file.no_text',
        'knowledge.file.header_required',
        'knowledge.file.too_many_pages',
        'knowledge.file.too_many_characters',
        'knowledge.file.too_many_rows',
        'knowledge.file.too_many_columns',
        'knowledge.file.cell_too_long',
        'knowledge.file.archive_too_large',
        'knowledge.file.name.required',
        'knowledge.file.name.unreadable',
        'knowledge.file.name.too_long',
        'knowledge.title.required',
        'knowledge.title.too_long',
        'knowledge.title.control_characters',
        'knowledge.content.required',
        'knowledge.content.too_long',
        'knowledge.content.control_characters',
        'knowledge.document.already_enabled',
        'knowledge.document.already_disabled',
        'knowledge.indexing.in_progress',
        'knowledge.index.too_many_chunks',
        'knowledge.source.missing',
        'knowledge.assignment.document_unknown',
        'knowledge.assignment.too_many',
        'knowledge.search.question.required',
        'knowledge.search.question.too_long',
        'entitlement.knowledgedocuments.exhausted',
      ].sort(),
    );
  });

  it('never shows the server sentence for a code it knows', () => {
    for (const code of Object.keys(KNOWLEDGE_PROBLEM_WORDS)) {
      expect(toApiError(refusal(code, 422, 5)).message).not.toContain('The English sentence');
    }
  });

  it.each([
    ['knowledge.file.too_many_pages', 500, 'more than 500 pages'],
    ['knowledge.file.too_many_characters', 2000000, 'more than 2,000,000 characters'],
    ['knowledge.file.too_many_rows', 50000, 'more than 50,000 rows'],
    ['knowledge.file.too_many_columns', 100, 'more than 100 columns'],
    ['knowledge.file.cell_too_long', 8000, 'longer than 8,000 characters'],
  ])('says the bound %s broke', (code, limit, words) => {
    expect(toApiError(refusal(code, 422, limit)).message).toContain(words);
  });

  it('says the size limit as a size', () => {
    expect(toApiError(refusal('knowledge.file.too_large', 413, 10485760)).message).toContain(
      'larger than 10 MB',
    );
  });

  describe('the archive limit, with its unit (BA-12)', () => {
    const archive = (limit?: number, limitUnit?: string) =>
      toApiError(
        new HttpErrorResponse({
          status: 422,
          error: {
            title: 'Refused',
            detail: 'The English sentence from the server.',
            code: 'knowledge.file.archive_too_large',
            limit,
            limitUnit,
          },
        }),
      );

    it('counts the files inside the document', () => {
      const error = archive(512, 'entries');

      expect(error.limitUnit).toBe('entries');
      expect(error.message).toBe(
        'Once opened, this Word document holds more than 512 files. Split it, or save a simpler copy.',
      );
      expect(archive(1, 'entries').message).toContain('more than 1 file.');
    });

    it('says an opened size as a size', () => {
      expect(archive(67108864, 'bytes').message).toBe(
        'Once opened, this Word document is larger than 64 MB. Split it, or save a simpler copy.',
      );
    });

    it("keeps today's sentence without a unit, or with one it does not know", () => {
      const today =
        'This Word document is too large once opened. Split it, or save a simpler copy.';

      expect(archive(512).message).toBe(today);
      expect(archive(512, 'parsecs').message).toBe(today);
      expect(archive(512, 'parsecs').limitUnit).toBeUndefined();
      expect(archive(undefined, 'entries').message).toBe(today);
    });
  });

  it('still says something true when a bound arrives without its number', () => {
    expect(toApiError(refusal('knowledge.file.too_many_pages', 422)).message).toContain(
      'past one of the limits',
    );
  });

  it.each([
    ['knowledge.file.encrypted', 'protected with a password'],
    ['knowledge.file.type_mismatch', 'does not match its type'],
    ['knowledge.file.no_text', 'No text could be found'],
    ['knowledge.indexing.in_progress', 'being indexed right now'],
    ['knowledge.source.missing', 'no file to replace'],
  ])('words %s by its code', (code, words) => {
    expect(toApiError(refusal(code, 422)).message).toContain(words);
  });

  it('gives an unregistered code the generic words, never the server sentence', () => {
    expect(toApiError(refusal('knowledge.some.future.code', 400)).message).toBe(
      'Some of the details were rejected. Check the highlighted fields and try again.',
    );
  });
});
