import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';

/**
 * The overview's own words (Phase 39.7a): the greeting, the day it is, and the
 * sentences the bento's tiles say. Kept beside the page, so none of it costs
 * the startup JavaScript anything: the overview is a lazily loaded route.
 */

const FSI = String.fromCharCode(0x2068);
const PDI = String.fromCharCode(0x2069);

/** The part of the day a greeting is for, from the reader's own clock. */
export type DayPart = 'morning' | 'afternoon' | 'evening';

/** Morning from 05:00, afternoon from 12:00, evening from 18:00 until 05:00. */
export function dayPart(hour: number): DayPart {
  if (hour >= 5 && hour < 12) {
    return 'morning';
  }

  return hour >= 12 && hour < 18 ? 'afternoon' : 'evening';
}

/**
 * "Good afternoon, Rana", or "Good afternoon" for a reader with no display
 * name: the greeting never falls back to an email address.
 *
 * Arabic has two greetings, not three: «صباح الخير» until noon and
 * «مساء الخير» after it, so afternoon and evening share one.
 */
export function greeting(part: DayPart, name: string | null): string {
  const trimmed = name?.trim() ?? '';
  // The name keeps its own direction inside either language's sentence:
  // first-strong isolate … pop directional isolate (UAX #9).
  const named = `${FSI}${trimmed}${PDI}`;

  if (trimmed === '') {
    switch (part) {
      case 'morning':
        return $localize`:@@overview.greeting.morning:Good morning`;
      case 'afternoon':
        return $localize`:@@overview.greeting.afternoon:Good afternoon`;
      case 'evening':
        return $localize`:@@overview.greeting.evening:Good evening`;
    }
  }

  switch (part) {
    case 'morning':
      return $localize`:@@overview.greeting.morning.named:Good morning, ${named}:name:`;
    case 'afternoon':
      return $localize`:@@overview.greeting.afternoon.named:Good afternoon, ${named}:name:`;
    case 'evening':
      return $localize`:@@overview.greeting.evening.named:Good evening, ${named}:name:`;
  }
}

/** "Sunday, 15 November" · «الأحد، 15 نوفمبر»: the reader's day, Latin digits, Gregorian. */
export function todayWords(language: AppLanguage, now: Date): string {
  return new Intl.DateTimeFormat(language, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    numberingSystem: 'latn',
    calendar: 'gregory',
  }).format(now);
}

/**
 * The share of the period's conversations the AI answered with nobody stepping
 * in (Phase 39.1, BA39-1), or null when no conversation started: a share of
 * nothing is not 0%.
 */
export function answeredShare(answered: number, started: number): number | null {
  return started > 0 ? Math.min(1, Math.max(0, answered / started)) : null;
}

/** "1,002 of 1,284": the two counts behind the ring. */
export function answeredCounts(answered: number, started: number, language: AppLanguage): string {
  const a = formatNumber(language, answered);
  const s = formatNumber(language, started);

  return $localize`:@@overview.answered.counts:${a}:answered: of ${s}:started:`;
}

/** "The 7 days before: 74%", or why there is no earlier share to show. */
export function answeredBefore(
  answered: number,
  started: number,
  earlierData: boolean,
  language: AppLanguage,
): string {
  const share = answeredShare(answered, started);

  if (share === null) {
    return earlierData
      ? $localize`:@@overview.answered.before.none:No conversations started the 7 days before`
      : $localize`:@@analytics.change.noEarlier:No earlier data to compare`;
  }

  const pct = formatNumber(language, share, { style: 'percent', maximumFractionDigits: 0 });

  return $localize`:@@overview.answered.before:The 7 days before: ${pct}:percent:`;
}

/** One channel's share of the conversations started, for the channels tile. */
export interface ChannelShare {
  readonly channel: string;
  readonly count: number;
  readonly share: number;
}

/**
 * The channels' shares of conversations started, largest first, and only the
 * channels where one started. The shares are the server's counts over their
 * sum, nothing else.
 */
export function channelShares(
  channels: readonly { readonly channel: string; readonly conversationsStarted: number }[],
): readonly ChannelShare[] {
  const used = channels.filter((c) => c.conversationsStarted > 0);
  const total = used.reduce((sum, c) => sum + c.conversationsStarted, 0);

  return used
    .map((c) => ({
      channel: c.channel,
      count: c.conversationsStarted,
      share: total > 0 ? c.conversationsStarted / total : 0,
    }))
    .sort((a, b) => b.count - a.count || a.channel.localeCompare(b.channel));
}
