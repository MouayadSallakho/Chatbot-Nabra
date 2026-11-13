import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The notification screens' refusals, by the server's stable `code` (Phase 38.7b,
 * worded in 38.8).
 *
 * Registered by the notification routes. Neither can be caused by what a reader
 * chose: both say what to do next. An all-zero notification id stays a 404
 * ("not yours"), worded as every 404 is.
 */
export const NOTIFICATION_PROBLEM_WORDS: ProblemCatalogue = {
  'notifications.page.invalid': $localize`:@@problem.notifications.page.invalid:This page of notifications could not be shown. Go back to the first page.`,
  'notifications.preferences.incomplete': $localize`:@@problem.notifications.preferences.incomplete:These preferences could not be saved. Reload the page and try again. Your choices are still selected.`,
};
