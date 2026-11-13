import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { REPORT_PROBLEM_WORDS } from './report-problems';

/** Every reports and schedules code; the Arabic is proved in `arabic-render.spec.ts`. */
describe('REPORT_PROBLEM_WORDS', () => {
  const refusal = (code: string, status: number) =>
    new HttpErrorResponse({
      status,
      error: { title: 'Refused', detail: 'The English sentence from the server.', code },
    });

  beforeAll(() => registerProblemWords(REPORT_PROBLEM_WORDS));

  const cases: readonly (readonly [string, number, string])[] = [
    ['reports.type.invalid', 400, 'not one Nabra offers'],
    ['reports.format.invalid', 400, 'Choose CSV, Excel or PDF.'],
    ['reports.format.unsupported', 400, 'not offered in that format'],
    ['reports.frequency.invalid', 400, 'Choose weekly or monthly.'],
    ['reports.client_request_id.required', 400, 'could not be requested'],
    ['reports.client_request_id.reused', 409, 'could not be requested'],
    ['reports.range.invalid', 400, 'the last 7, 30 or 90 days, or two dates'],
    ['reports.range.conflict', 400, 'could not be requested'],
    ['reports.range.dates_required', 400, 'both a start date and an end date'],
    ['reports.period.future', 400, 'cannot end after today'],
    ['reports.period.reversed', 400, 'The end date is before the start date.'],
    ['reports.period.too_long', 400, 'longer than a report can cover'],
    ['reports.period.too_early', 400, 'before the earliest date reports cover'],
    ['reports.page.invalid', 400, 'This page of reports could not be shown.'],
    ['reports.id.invalid', 400, 'This report could not be found.'],
    ['reports.file.not_ready', 409, 'still being prepared'],
    ['reports.file.failed', 409, 'could not be prepared, so there is no file'],
    ['reports.file.expired', 410, 'no longer kept. Generate the report again.'],
    ['reports.file.missing', 410, 'no longer kept. Generate the report again.'],
    ['reports.schedule.id.invalid', 400, 'This schedule could not be found.'],
    ['reports.schedule.name.required', 400, 'Give this schedule a name.'],
    ['reports.schedule.name.too_long', 400, 'This name is too long.'],
    ['reports.schedule.name.control_characters', 400, 'one line of plain text'],
    ['reports.schedule.limit_reached', 409, 'Remove one before adding another.'],
    ['reports.schedule.enabled_required', 400, 'could not be switched on or off'],
  ];

  it('covers every reports and schedules code in problem-codes.md (Phase 38.7b)', () => {
    expect(Object.keys(REPORT_PROBLEM_WORDS).sort()).toEqual(cases.map(([code]) => code).sort());
  });

  it.each(cases)('words %s by its code, not by the server sentence', (code, status, words) => {
    const error = toApiError(refusal(code, status));

    expect(error.code).toBe(code);
    expect(error.message).toContain(words);
    expect(error.message).not.toContain('The English sentence');
  });

  it('gives an unregistered code the generic words, never the server sentence', () => {
    expect(toApiError(refusal('reports.some.future.code', 400)).message).toBe(
      'Some of the details were rejected. Check the highlighted fields and try again.',
    );
  });
});
