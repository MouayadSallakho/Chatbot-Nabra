import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The billing screen's refusals, by the server's stable `code`.
 *
 * Every one says that nothing was charged, because that is the first thing a
 * person starting a purchase wants to know when it stops. Registered by the
 * billing routes, so the words cost the startup JavaScript nothing.
 */
export const BILLING_PROBLEM_WORDS: ProblemCatalogue = {
  'billing.offer_id.invalid': $localize`:@@problem.billing.offer_id.invalid:No plan was chosen. Choose one and try again. Nothing was charged.`,
  'billing.checkout.unknownoffer': $localize`:@@problem.billing.checkout.unknownoffer:That plan is no longer on sale. Reload the page to see what is. Nothing was charged.`,
  'billing.checkout.offernotavailable': $localize`:@@problem.billing.checkout.offernotavailable:That plan has been withdrawn from sale. Nothing was charged.`,
  'billing.checkout.alreadysubscribed': $localize`:@@problem.billing.checkout.alreadysubscribed:This workspace already has a subscription. Change it from Manage payment and invoices. Nothing was charged.`,
  'billing.checkout.offernotpurchasable': $localize`:@@problem.billing.checkout.offernotpurchasable:That plan cannot be bought on this installation. Nothing was charged.`,
};
