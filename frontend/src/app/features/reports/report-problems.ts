import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/** For the rules a request built by this page cannot break: nothing the reader chose. */
const NOT_REQUESTED = $localize`:@@problem.reports.notRequested:This report could not be requested. Reload the page and try again.`;

/** For a list or a record the page addressed wrongly. */
const NOT_FOUND = $localize`:@@problem.reports.notFound:This report could not be found. Reload the page.`;

/** A file that is gone, whatever took it: the next step is the same. */
const FILE_GONE = $localize`:@@problem.reports.file.gone:This report's file is no longer kept. Generate the report again.`;

/**
 * The reports screens' refusals, by the server's stable `code` (Phase 38.7b,
 * worded in 38.8).
 *
 * Registered by the reports routes, so the words arrive with the feature and cost
 * the startup JavaScript nothing. A code that is not here keeps today's generic
 * wording, and the English `detail` is never what an Arabic reader sees.
 *
 * The file codes answer the download route, which the page reaches by a plain
 * link, so a browser usually shows them itself; they are worded for any screen
 * that reads them.
 */
export const REPORT_PROBLEM_WORDS: ProblemCatalogue = {
  'reports.type.invalid': $localize`:@@problem.reports.type.invalid:This report is not one Nabra offers. Reload the page and choose again.`,
  'reports.format.invalid': $localize`:@@problem.reports.format.invalid:Choose CSV, Excel or PDF.`,
  'reports.format.unsupported': $localize`:@@problem.reports.format.unsupported:This report is not offered in that format. Choose another format.`,
  'reports.frequency.invalid': $localize`:@@problem.reports.frequency.invalid:Choose weekly or monthly.`,
  'reports.client_request_id.required': NOT_REQUESTED,
  'reports.client_request_id.reused': NOT_REQUESTED,
  'reports.range.invalid': $localize`:@@problem.reports.range.invalid:Choose a period: the last 7, 30 or 90 days, or two dates.`,
  'reports.range.conflict': NOT_REQUESTED,
  'reports.range.dates_required': $localize`:@@problem.reports.range.dates_required:Choose both a start date and an end date.`,
  'reports.period.future': $localize`:@@problem.reports.period.future:The period cannot end after today. Choose an earlier end date.`,
  'reports.period.reversed': $localize`:@@problem.reports.period.reversed:The end date is before the start date. Swap them and try again.`,
  'reports.period.too_long': $localize`:@@problem.reports.period.too_long:This period is longer than a report can cover. Choose a shorter one.`,
  'reports.period.too_early': $localize`:@@problem.reports.period.too_early:This period starts before the earliest date reports cover. Choose a later start date.`,
  'reports.page.invalid': $localize`:@@problem.reports.page.invalid:This page of reports could not be shown. Go back to the first page.`,
  'reports.id.invalid': NOT_FOUND,
  'reports.file.not_ready': $localize`:@@problem.reports.file.not_ready:This report is still being prepared. Try the download again in a moment.`,
  'reports.file.failed': $localize`:@@problem.reports.file.failed:This report could not be prepared, so there is no file. Generate it again.`,
  'reports.file.expired': FILE_GONE,
  'reports.file.missing': FILE_GONE,
  'reports.schedule.id.invalid': $localize`:@@problem.reports.schedule.id.invalid:This schedule could not be found. Reload the page.`,
  'reports.schedule.name.required': $localize`:@@problem.reports.schedule.name.required:Give this schedule a name.`,
  'reports.schedule.name.too_long': $localize`:@@problem.reports.schedule.name.too_long:This name is too long. Shorten it and save again.`,
  'reports.schedule.name.control_characters': $localize`:@@problem.reports.schedule.name.control_characters:A name has to be one line of plain text.`,
  'reports.schedule.limit_reached': $localize`:@@problem.reports.schedule.limit_reached:This workspace has as many schedules as it may have. Remove one before adding another.`,
  'reports.schedule.enabled_required': $localize`:@@problem.reports.schedule.enabled_required:This schedule could not be switched on or off. Reload the page and try again.`,
};
