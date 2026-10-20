import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import type { SubscriptionStatus } from './billing-api';

/**
 * Billing's words, written once (Phase 36.10).
 *
 * The statuses are the server's own (normalised from the payment provider's).
 * Each has a word and a marker of its own: a hollow ring while awaiting the
 * first payment, an "i" for a trial, a tick for active, a triangle when a
 * payment failed, an octagon when suspended, a filled dot once ended.
 *
 * Which plan is in effect follows the server's policy (`GrantsPlan`): a
 * trial, an active subscription and one past due keep the subscribed plan;
 * awaiting payment, suspended and ended fall back to the deployment's
 * fallback plan. The page never infers either from the browser.
 */
interface StatusWords {
  readonly word: string;
  readonly badge: string;
  readonly sentence: string;
}

const STATUSES: Readonly<Record<SubscriptionStatus, StatusWords>> = {
  PendingActivation: {
    word: $localize`:@@billing.status.pending:Awaiting first payment`,
    badge: 'badge-neutral badge-draft',
    sentence: $localize`:@@billing.status.pending.sentence:The subscription has been started but its first payment has not completed. Until it does, the workspace stays on its current plan.`,
  },
  Trialing: {
    word: $localize`:@@billing.status.trialing:Trial`,
    badge: 'badge-info',
    sentence: $localize`:@@billing.status.trialing.sentence:The trial is running. The subscribed plan is available in full until it ends.`,
  },
  Active: {
    word: $localize`:@@billing.status.active:Active`,
    badge: 'badge-success',
    sentence: $localize`:@@billing.status.active.sentence:Payments are up to date.`,
  },
  PastDue: {
    word: $localize`:@@billing.status.pastDue:Payment failed`,
    badge: 'badge-warning',
    sentence: $localize`:@@billing.status.pastDue.sentence:A payment did not go through. The workspace keeps its plan during a grace period; update the payment method to keep it after that.`,
  },
  Suspended: {
    word: $localize`:@@billing.status.suspended:Suspended`,
    badge: 'badge-danger',
    sentence: $localize`:@@billing.status.suspended.sentence:The grace period ended without a successful payment, so the workspace moved to a smaller plan. Nothing was deleted.`,
  },
  Canceled: {
    word: $localize`:@@billing.status.canceled:Ended`,
    badge: 'badge-neutral',
    sentence: $localize`:@@billing.status.canceled.sentence:This subscription has ended and the workspace moved to a smaller plan. Nothing was deleted.`,
  },
};

/** A status in words; an unknown value is shown as the server sent it, neutrally. */
export function subscriptionStatus(status: string): StatusWords {
  return (
    STATUSES[status as SubscriptionStatus] ?? {
      word: status,
      badge: 'badge-neutral',
      sentence: '',
    }
  );
}

/** Whether a status keeps the subscribed plan (the server's `GrantsPlan`). */
export function grantsPlan(status: string): boolean {
  return status === 'Trialing' || status === 'Active' || status === 'PastDue';
}

/** "in 5 days" / "today": how long the grace period has left. */
export function daysLeft(iso: string, now: number, language: AppLanguage): string {
  const days = Math.ceil((new Date(iso).getTime() - now) / 86_400_000);

  if (days <= 0) {
    return $localize`:@@billing.daysLeft.today:ends today`;
  }

  const n = formatNumber(language, days);

  return pluralMessage(language, days, {
    zero: $localize`:@@billing.daysLeft.zero:ends today`,
    one: $localize`:@@billing.daysLeft.one:${n}:count: day left`,
    two: $localize`:@@billing.daysLeft.two:${n}:count: days left`,
    few: $localize`:@@billing.daysLeft.few:${n}:count: days left`,
    many: $localize`:@@billing.daysLeft.many:${n}:count: days left`,
    other: $localize`:@@billing.daysLeft.other:${n}:count: days left`,
  });
}

/** "per month" / "per year". */
export function cadenceWord(interval: string): string {
  return interval === 'Annual'
    ? $localize`:@@billing.cadence.annual:per year`
    : $localize`:@@billing.cadence.monthly:per month`;
}

/** "Monthly" / "Yearly", for the billing period fact. */
export function intervalWord(interval: string): string {
  return interval === 'Annual'
    ? $localize`:@@billing.interval.annual:Yearly`
    : $localize`:@@billing.interval.monthly:Monthly`;
}

/** "14-day trial, then the price above", with every Arabic plural form. */
export function trialWords(days: number, language: AppLanguage): string {
  const n = formatNumber(language, days);

  return pluralMessage(language, days, {
    zero: $localize`:@@billing.trial.zero:No trial`,
    one: $localize`:@@billing.trial.one:${n}:count: day of trial, then the price shown`,
    two: $localize`:@@billing.trial.two:${n}:count: days of trial, then the price shown`,
    few: $localize`:@@billing.trial.few:${n}:count: days of trial, then the price shown`,
    many: $localize`:@@billing.trial.many:${n}:count: days of trial, then the price shown`,
    other: $localize`:@@billing.trial.other:${n}:count: days of trial, then the price shown`,
  });
}
