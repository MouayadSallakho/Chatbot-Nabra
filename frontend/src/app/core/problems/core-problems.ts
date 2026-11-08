import type { ApiError } from '@app/core/api/api-error';
import { PASSWORD_MIN_LENGTH } from '@app/core/auth/password-policy';
import { AppLanguage } from '@app/core/i18n/language';
import { PluralForms, pluralMessage } from '@app/core/i18n/plural';
import type { ProblemCatalogue } from './problem-words';

/** Too many attempts for this address or network. Comes with `Retry-After`. */
export const THROTTLED_CODE = 'auth.throttled';

/** The limiter could not decide, so the request was refused. */
export const THROTTLE_UNAVAILABLE_CODE = 'auth.throttle.unavailable';

/** The prefix of the codes for a password the policy refuses. */
const PASSWORD_CODE_PREFIX = 'auth.password.';

/** Identity's password rules. Today's policy only ever breaks the first. */
const PASSWORD_RULES: Readonly<Record<string, string>> = {
  'auth.password.too_short': $localize`:@@problem.auth.password.too_short:Use at least ${PASSWORD_MIN_LENGTH}:count: characters.`,
  'auth.password.requires_non_alphanumeric': $localize`:@@problem.auth.password.requires_non_alphanumeric:Add at least one character that is not a letter or a digit.`,
  'auth.password.requires_digit': $localize`:@@problem.auth.password.requires_digit:Add at least one digit.`,
  'auth.password.requires_lower': $localize`:@@problem.auth.password.requires_lower:Add at least one lowercase letter.`,
  'auth.password.requires_upper': $localize`:@@problem.auth.password.requires_upper:Add at least one uppercase letter.`,
  'auth.password.requires_unique_chars': $localize`:@@problem.auth.password.requires_unique_chars:Use more different characters.`,
};

/** For a password rule this build has no sentence for. */
const PASSWORD_REFUSED = $localize`:@@resetPassword.password.refused:This password cannot be used. Choose a different one.`;

/**
 * One sentence per broken password rule, once each.
 *
 * Every rule at once, so nobody fixes them one round trip at a time. Empty when
 * the refusal is not about the password.
 */
export function passwordProblemLines(failure: Pick<ApiError, 'code' | 'codes'>): readonly string[] {
  const rules = [failure.code, ...(failure.codes ?? [])].filter(
    (code): code is string => code?.startsWith(PASSWORD_CODE_PREFIX) ?? false,
  );

  return [...new Set(rules.map((code) => PASSWORD_RULES[code] ?? PASSWORD_REFUSED))];
}

function throttledMinutes(count: number): PluralForms {
  return {
    zero: $localize`:@@problem.auth.throttled.minutes.zero:Too many attempts. Try again in ${count}:count: minutes.`,
    one: $localize`:@@problem.auth.throttled.minutes.one:Too many attempts. Try again in ${count}:count: minute.`,
    two: $localize`:@@problem.auth.throttled.minutes.two:Too many attempts. Try again in ${count}:count: minutes.`,
    few: $localize`:@@problem.auth.throttled.minutes.few:Too many attempts. Try again in ${count}:count: minutes.`,
    many: $localize`:@@problem.auth.throttled.minutes.many:Too many attempts. Try again in ${count}:count: minutes.`,
    other: $localize`:@@problem.auth.throttled.minutes.other:Too many attempts. Try again in ${count}:count: minutes.`,
  };
}

function throttledSeconds(count: number): PluralForms {
  return {
    zero: $localize`:@@problem.auth.throttled.seconds.zero:Too many attempts. Try again in ${count}:count: seconds.`,
    one: $localize`:@@problem.auth.throttled.seconds.one:Too many attempts. Try again in ${count}:count: second.`,
    two: $localize`:@@problem.auth.throttled.seconds.two:Too many attempts. Try again in ${count}:count: seconds.`,
    few: $localize`:@@problem.auth.throttled.seconds.few:Too many attempts. Try again in ${count}:count: seconds.`,
    many: $localize`:@@problem.auth.throttled.seconds.many:Too many attempts. Try again in ${count}:count: seconds.`,
    other: $localize`:@@problem.auth.throttled.seconds.other:Too many attempts. Try again in ${count}:count: seconds.`,
  };
}

/**
 * "Too many attempts. Try again in 15 minutes."
 *
 * Whole minutes, rounded up, from a minute on; seconds under it. With no usable
 * `Retry-After` it says the honest vague thing instead of inventing a number.
 */
export function throttledMessage(seconds: number | undefined, language: AppLanguage): string {
  if (seconds === undefined || seconds <= 0) {
    return $localize`:@@problem.auth.throttled.noWait:Too many attempts. Wait a moment and try again.`;
  }

  if (seconds < 60) {
    return pluralMessage(language, seconds, throttledSeconds(seconds));
  }

  const minutes = Math.ceil(seconds / 60);

  return pluralMessage(language, minutes, throttledMinutes(minutes));
}

/**
 * The refusals the first screen of the application can meet.
 *
 * Small on purpose: it is in the startup JavaScript. `validation.failed` and
 * `state.conflict` are deliberately not here. They get the generic words of their
 * kind in Arabic, and an English page keeps the server's sentence until the last
 * area has moved (38.5), so a screen not yet moved says what it says today.
 * `precondition.failed` and `precondition.required` are worded by their status.
 */
export const CORE_PROBLEM_WORDS: ProblemCatalogue = {
  ...PASSWORD_RULES,

  'antiforgery.invalid': $localize`:@@apiError.antiforgery:This page's security check had expired. Nothing was changed. The check has been renewed, so try again.`,
  'language.unsupported': $localize`:@@problem.language.unsupported:That language is not available.`,
  'precondition.if_match.invalid': $localize`:@@apiError.preconditionRequired:This page needs the latest version of the item before it can save. Reload it and try again.`,

  'auth.credentials.invalid': $localize`:@@problem.auth.credentials.invalid:The email address or password is incorrect.`,
  'auth.registration.incomplete': $localize`:@@problem.auth.registration.incomplete:Enter an email address and a password.`,
  'auth.registration.rejected': $localize`:@@problem.auth.registration.rejected:This account could not be created. Check your details and try again.`,
  'auth.email.taken': $localize`:@@problem.auth.email.taken:That email address is already in use. Sign in, or use a different address.`,
  'auth.email.invalid': $localize`:@@problem.auth.email.invalid:That email address cannot be used. Check it and try again.`,
  [THROTTLED_CODE]: ({ retryAfterSeconds, language }) =>
    throttledMessage(retryAfterSeconds, language),
  [THROTTLE_UNAVAILABLE_CODE]: $localize`:@@problem.auth.throttle.unavailable:Signing in is unavailable for a moment. Nothing was changed. Try again shortly.`,

  'invitations.no_longer_usable': $localize`:@@apiError.gone:This invitation is no longer available. Ask whoever invited you for a new one.`,
  'invitations.account.mismatch': $localize`:@@invitation.otherAccount:This invitation was sent to a different account. Sign out and open the link again as the address it was sent to.`,
};
