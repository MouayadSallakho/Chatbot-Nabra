import type { ApiError } from '@app/core/api/api-error';
import type { AppLanguage } from '@app/core/i18n/language';
import { PluralForms, pluralMessage } from '@app/core/i18n/plural';

/** Which public surface a sentence is for: the two say "not found" differently. */
export type VisitorSurface = 'hosted' | 'embed';

/**
 * Says what happened in words a customer can act on (Phase 36.9: one copy for
 * the hosted page and the widget, in the visitor's language).
 *
 * <strong>The shared copy is written for administrators, and here that is
 * wrong.</strong> "This needs an AI provider, and none is configured" tells
 * somebody who came to ask about a delivery about this platform's deployment. A
 * customer needs to know that the answer did not arrive, whether what they wrote
 * was kept, and that trying again is worth it.
 *
 * <strong>The causes are deliberately not told apart.</strong> An unconfigured
 * provider, a timeout, an unusable answer and an unreachable protection store
 * are one thing to a visitor (no answer right now), and saying which would
 * report on the business's infrastructure to the public
 * (docs/architecture/public-chat-protection.md, P26 and P27).
 */
export function visitorFailure(
  failure: ApiError,
  surface: VisitorSurface,
  language: AppLanguage,
): ApiError {
  switch (failure.kind) {
    // A 503 may mean nothing was stored (Phase 34), so it must not say the
    // message was received.
    case 'providerUnavailable':
      return {
        ...failure,
        message: $localize`:@@visitor.error.noAnswer:Sorry, no answer came back just now. You can try sending it again in a moment.`,
      };

    // A timeout or an unusable answer happens after the message was stored.
    case 'providerTimeout':
    case 'invalidResponse':
      return {
        ...failure,
        message: $localize`:@@visitor.error.noAnswerReceived:Sorry, no answer came back just now. Your message was received, so you can try sending it again in a moment.`,
      };

    // Its own copy: nothing failed and nothing was lost. It says nothing about
    // quotas, budgets or how the limit is counted.
    case 'rateLimited':
      return {
        ...failure,
        message:
          $localize`:@@visitor.error.tooQuickly:You are sending messages too quickly.` +
          ' ' +
          waitAdvice(failure.retryAfterSeconds, language),
      };

    case 'notFound':
      return {
        ...failure,
        message:
          surface === 'hosted'
            ? $localize`:@@visitor.error.linkNotValid:This chat link is not valid. Check the link you were given, or contact the business another way.`
            : $localize`:@@visitor.error.chatGone:This chat is no longer available. Please reload the page and try again.`,
      };

    default:
      return failure;
  }
}

/**
 * How long to wait, in words rather than a countdown.
 *
 * Rounded up to whole minutes past a minute and a half, because "wait 97
 * seconds" invites somebody to sit and count. With no usable `Retry-After` it
 * says the honest vague thing instead of inventing a number. Full sentences per
 * plural category: Arabic agreement changes with the count.
 */
export function waitAdvice(seconds: number | undefined, language: AppLanguage): string {
  if (seconds === undefined || !Number.isFinite(seconds) || seconds <= 0) {
    return $localize`:@@visitor.wait.moment:Please wait a moment and try again.`;
  }

  if (seconds <= 90) {
    const count = Math.ceil(seconds);

    return pluralMessage(language, count, secondsForms(count));
  }

  const count = Math.ceil(seconds / 60);

  return pluralMessage(language, count, minutesForms(count));
}

function secondsForms(count: number): PluralForms {
  return {
    zero: $localize`:@@visitor.wait.seconds.zero:Please wait about ${count}:count: seconds and try again.`,
    one: $localize`:@@visitor.wait.seconds.one:Please wait about ${count}:count: second and try again.`,
    two: $localize`:@@visitor.wait.seconds.two:Please wait about ${count}:count: seconds and try again.`,
    few: $localize`:@@visitor.wait.seconds.few:Please wait about ${count}:count: seconds and try again.`,
    many: $localize`:@@visitor.wait.seconds.many:Please wait about ${count}:count: seconds and try again.`,
    other: $localize`:@@visitor.wait.seconds.other:Please wait about ${count}:count: seconds and try again.`,
  };
}

function minutesForms(count: number): PluralForms {
  return {
    zero: $localize`:@@visitor.wait.minutes.zero:Please wait about ${count}:count: minutes and try again.`,
    one: $localize`:@@visitor.wait.minutes.one:Please wait about ${count}:count: minute and try again.`,
    two: $localize`:@@visitor.wait.minutes.two:Please wait about ${count}:count: minutes and try again.`,
    few: $localize`:@@visitor.wait.minutes.few:Please wait about ${count}:count: minutes and try again.`,
    many: $localize`:@@visitor.wait.minutes.many:Please wait about ${count}:count: minutes and try again.`,
    other: $localize`:@@visitor.wait.minutes.other:Please wait about ${count}:count: minutes and try again.`,
  };
}
