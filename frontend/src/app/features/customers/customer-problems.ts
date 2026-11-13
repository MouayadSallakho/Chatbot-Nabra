import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The customers screens' refusals, by the server's stable `code` (Phase 38.7b,
 * worded in 38.8).
 *
 * Registered by the customers routes, so the words arrive with the feature and
 * cost the startup JavaScript nothing. A code that is not here keeps today's
 * generic wording, and the English `detail` is never what an Arabic reader sees.
 * The customer page still says a lost race and a combined record in its own
 * words (`customer-detail.ts`), which these do not replace.
 */
export const CUSTOMER_PROBLEM_WORDS: ProblemCatalogue = {
  'customers.page.invalid': $localize`:@@problem.customers.page.invalid:This page of customers could not be shown. Go back to the first page.`,
  'customers.id.invalid': $localize`:@@problem.customers.id.invalid:This customer could not be found. Open them again from the customer list.`,
  'customers.display_name.too_long': $localize`:@@problem.customers.display_name.too_long:This name is too long. Shorten it and save again.`,
  'customers.display_name.control_characters': $localize`:@@problem.customers.display_name.control_characters:A name has to be one line of plain text.`,
  'customers.merge.target_required': $localize`:@@problem.customers.merge.target_required:Choose the customer to combine with first.`,
  'customers.merge.self': $localize`:@@problem.customers.merge.self:A customer cannot be combined with itself. Choose another one.`,
  'customers.merge.not_in_workspace': $localize`:@@problem.customers.merge.not_in_workspace:One of these customers is no longer in this workspace. Reload the page and choose again.`,
  'customers.merged': $localize`:@@problem.customers.merged:This customer has already been combined with another one. Reload the page to see where their history went.`,
};
