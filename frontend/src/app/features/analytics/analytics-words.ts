import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import {
  formatInZone,
  formatLocalDate,
  isLocalMidnight,
  localDate,
  zonePhrase,
} from '@app/core/i18n/time-zone';
import type { ChartPoint, ChartSeries } from '@app/shared/charts/chart-model';
import type {
  AnalyticsDashboard,
  AnalyticsDay,
  AnalyticsPeriod,
  AnalyticsRange,
} from './analytics-api';

/**
 * The numbers' words, written once (Phase 36.7).
 *
 * Overview and Analytics read the same dashboard endpoint and name the same
 * few things -- who wrote a message, how a period is bounded, what "today, so
 * far" means -- so a reader meets one word for each on both pages, in English
 * and in Arabic. Counts are written as "label: number" or as a figure under its
 * label wherever a sentence would need a counted noun; where a real sentence is
 * needed, it has all six Arabic plural forms.
 */

/** The three senders, in stacking order: customers on the baseline, then the two voices. */
export function senderSeries(): readonly ChartSeries[] {
  return [
    {
      key: 'customer',
      label: $localize`:@@analytics.series.customers:Customers`,
      tone: 'customer',
    },
    { key: 'human', label: $localize`:@@analytics.series.team:Team`, tone: 'human' },
    { key: 'ai', label: $localize`:@@analytics.series.ai:AI`, tone: 'ai' },
  ];
}

/**
 * Whether a period ends part-way through a day, as the current one does: it
 * runs until the moment of the read, so its last day is "so far".
 */
export function stillRunning(period: AnalyticsPeriod): boolean {
  return !isLocalMidnight(period.to, period.timeZone);
}

/**
 * The days a dashboard returned, as chart points: local days of the
 * workspace's zone, the last one marked if it is still running.
 */
export function dailyPoints(
  dashboard: AnalyticsDashboard,
  language: AppLanguage,
): readonly ChartPoint[] {
  const today = localDate(dashboard.period.to, dashboard.period.timeZone);
  const running = stillRunning(dashboard.period);

  return dashboard.dailyActivity.map((day) => ({
    key: day.date,
    label: localDay(day.date, language, 'dayMonth'),
    longLabel: localDay(day.date, language, 'longDate'),
    partial: running && day.date === today,
    values: dayValues(day),
  }));
}

export function conversationActivity(dashboard: AnalyticsDashboard | null, language: AppLanguage) {
  const points = dashboard ? dailyPoints(dashboard, language) : [];
  const previous = dashboard?.previous?.dailyActivity;
  const series: ChartSeries[] = [
    {
      key: 'conversations',
      label: $localize`:@@analytics.series.conversations:Conversations started`,
      tone: 'current',
    },
  ];
  if (!previous?.length || previous.length !== points.length) return { points, series };
  series.push({
    key: 'previous',
    label: $localize`:@@analytics.days.previous:Previous period`,
    tone: 'muted',
  });
  return {
    series,
    points: points.map((point, index) => ({
      ...point,
      longLabel: point.longLabel + ' / ' + localDay(previous[index].date, language, 'longDate'),
      values: { ...point.values, previous: previous[index].conversationsStarted },
    })),
  };
}

/** One day's counts under the series keys, plus conversations for tables that carry them. */
export function dayValues(day: AnalyticsDay): Readonly<Record<string, number>> {
  return {
    customer: day.customerMessages,
    human: day.humanMessages,
    ai: day.aiMessages,
    conversations: day.conversationsStarted,
  };
}

/**
 * A `yyyy-MM-dd` day of the workspace's zone in words.
 *
 * The buckets are already the workspace's local dates, so the date is written
 * from its parts: letting the browser read a bare date in its own zone would
 * shift every label by a day for half the world.
 */
export function localDay(
  date: string,
  language: AppLanguage,
  style: 'dayMonth' | 'longDate' | 'date' = 'dayMonth',
): string {
  return formatLocalDate(language, date, style);
}

/** The period's words for the selector: "7 days". */
export function rangeWord(range: AnalyticsRange): string {
  switch (range) {
    case '7d':
      return $localize`:@@analytics.range.7d:7 days`;
    case '30d':
      return $localize`:@@analytics.range.30d:30 days`;
    case '90d':
      return $localize`:@@analytics.range.90d:90 days`;
  }
}

/**
 * "Oct 12, 2026 – Oct 18, 2026, whole days in Asia/Riyadh time. Today is
 * counted until 7:27 PM." Every date is a day of the workspace's zone, and the
 * time is the moment of the read, as it reads there.
 */
export function periodSentence(dashboard: AnalyticsDashboard, language: AppLanguage): string {
  const period = dashboard.period;
  const first = dashboard.dailyActivity[0]?.date ?? localDate(period.from, period.timeZone);
  const last = dashboard.dailyActivity.at(-1)?.date ?? localDate(period.to, period.timeZone);
  const from = localDay(first, language, 'date');
  const to = localDay(last, language, 'date');
  const zone = zonePhrase(period.timeZone);

  if (!stillRunning(period)) {
    return $localize`:@@analytics.period.whole:${from}:from: – ${to}:to:, whole days ${zone}:zone:.`;
  }

  const until = formatInZone(language, period.to, 'time', period.timeZone);

  return $localize`:@@analytics.period.running:${from}:from: – ${to}:to:, whole days ${zone}:zone:. Today is counted until ${until}:time:.`;
}

/** The days a period covers: its first and last local dates. */
function periodDays(period: AnalyticsPeriod): { readonly first: string; readonly last: string } {
  const lastInstant = isLocalMidnight(period.to, period.timeZone)
    ? Date.parse(period.to) - 1
    : period.to;

  return {
    first: localDate(period.from, period.timeZone),
    last: localDate(lastInstant, period.timeZone),
  };
}

/** "Compared with Oct 5, 2026 – Oct 11, 2026." Null without a previous period. */
export function comparisonSentence(
  dashboard: AnalyticsDashboard,
  language: AppLanguage,
): string | null {
  if (!dashboard.previous) {
    return null;
  }

  const days = periodDays(dashboard.previous.period);
  const from = localDay(days.first, language, 'date');
  const to = localDay(days.last, language, 'date');

  return $localize`:@@analytics.compare.period:Compared with ${from}:from: – ${to}:to:.`;
}

/** "Data since Mar 3, 2026.", or the sentence for a workspace with no conversation yet. */
export function dataSinceSentence(firstActivityOn: string | null, language: AppLanguage): string {
  if (firstActivityOn === null) {
    return $localize`:@@analytics.since.none:No conversation has started in this workspace yet.`;
  }

  const date = localDay(firstActivityOn, language, 'date');

  return $localize`:@@analytics.since:Data since ${date}:date:.`;
}

/** Which way a figure moved against the previous period. */
export type ChangeDirection = 'up' | 'down' | 'same' | 'none';

/** A figure's change in words, with a glyph that only repeats the words. */
export interface FigureChange {
  readonly direction: ChangeDirection;

  /** ↑, ↓ or =; empty when there is nothing to compare. Decorative: the text says it. */
  readonly glyph: string;
  readonly text: string;
}

/**
 * A figure against the same figure in the previous period, in words.
 *
 * The direction is said in words ("Up 12% on the previous period"), never by
 * colour alone, and no colour says whether a rise is good: more failed replies
 * is a rise too. While the current period is still running its figures are
 * "so far". A previous figure of zero gives no percentage: "No earlier data
 * to compare" when the workspace had no activity before this period, otherwise
 * "None in the previous period".
 */
export function figureChange(
  current: number,
  previous: number,
  options: { readonly soFar: boolean; readonly earlierData: boolean },
  language: AppLanguage,
): FigureChange {
  if (previous === 0) {
    return {
      direction: 'none',
      glyph: '',
      text: options.earlierData
        ? $localize`:@@analytics.change.zeroBefore:None in the previous period`
        : $localize`:@@analytics.change.noEarlier:No earlier data to compare`,
    };
  }

  if (current === previous) {
    return {
      direction: 'same',
      glyph: '=',
      text: options.soFar
        ? $localize`:@@analytics.change.sameSoFar:So far, the same as the previous period`
        : $localize`:@@analytics.change.same:The same as the previous period`,
    };
  }

  const ratio = Math.abs(current - previous) / previous;
  const pct = formatNumber(language, ratio, {
    style: 'percent',
    maximumFractionDigits: ratio < 0.1 ? 1 : 0,
  });

  if (current > previous) {
    return {
      direction: 'up',
      glyph: '↑',
      text: options.soFar
        ? $localize`:@@analytics.change.upSoFar:So far, up ${pct}:percent: on the previous period`
        : $localize`:@@analytics.change.up:Up ${pct}:percent: on the previous period`,
    };
  }

  return {
    direction: 'down',
    glyph: '↓',
    text: options.soFar
      ? $localize`:@@analytics.change.downSoFar:So far, down ${pct}:percent: on the previous period`
      : $localize`:@@analytics.change.down:Down ${pct}:percent: on the previous period`,
  };
}

/**
 * Whether the workspace had any activity before a period started: a previous
 * figure of zero then means "none", not "no data".
 */
export function hadEarlierData(dashboard: AnalyticsDashboard): boolean {
  const first = dashboard.firstActivityOn;

  return first !== null && first < localDate(dashboard.period.from, dashboard.period.timeZone);
}

/** The keys the four period figures go by on both screens. */
export type SummaryKey = 'conversations' | 'customer' | 'ai' | 'human';

/** The four period figures' changes, or null when the comparison was not asked for. */
export function summaryChanges(
  dashboard: AnalyticsDashboard,
  language: AppLanguage,
): Readonly<Record<SummaryKey, FigureChange>> | null {
  const previous = dashboard.previous;

  if (!previous) {
    return null;
  }

  const options = { soFar: stillRunning(dashboard.period), earlierData: hadEarlierData(dashboard) };
  const now = dashboard.summary;
  const before = previous.summary;

  return {
    conversations: figureChange(
      now.conversationsStarted,
      before.conversationsStarted,
      options,
      language,
    ),
    customer: figureChange(now.customerMessages, before.customerMessages, options, language),
    ai: figureChange(now.aiMessages, before.aiMessages, options, language),
    human: figureChange(now.humanMessages, before.humanMessages, options, language),
  };
}

/** What a messages-per-day chart says in one sentence. */
export function messagesSummary(dashboard: AnalyticsDashboard, language: AppLanguage): string {
  const n = (value: number) => formatNumber(language, value);
  const busiest = [...dashboard.dailyActivity].sort(
    (a, b) => total(b) - total(a) || b.date.localeCompare(a.date),
  )[0];

  if (!busiest || total(busiest) === 0) {
    return $localize`:@@analytics.messages.summaryNone:No messages were sent on any day in this period.`;
  }

  const customers = n(dashboard.summary.customerMessages);
  const team = n(dashboard.summary.humanMessages);
  const ai = n(dashboard.summary.aiMessages);
  const day = localDay(busiest.date, language, 'longDate');
  const most = n(total(busiest));

  return $localize`:@@analytics.messages.summary:Messages in this period. Customers: ${customers}:customers:, team: ${team}:team:, AI: ${ai}:ai:. Busiest day: ${day}:day: (${most}:most:).`;
}

/** What a conversations-per-day chart says in one sentence. */
export function conversationsSummary(dashboard: AnalyticsDashboard, language: AppLanguage): string {
  const busiest = [...dashboard.dailyActivity].sort(
    (a, b) => b.conversationsStarted - a.conversationsStarted || b.date.localeCompare(a.date),
  )[0];

  if (!busiest || busiest.conversationsStarted === 0) {
    return $localize`:@@analytics.conversations.summaryNone:No conversation started on any day in this period.`;
  }

  const count = formatNumber(language, dashboard.summary.conversationsStarted);
  const day = localDay(busiest.date, language, 'longDate');
  const most = formatNumber(language, busiest.conversationsStarted);

  return $localize`:@@analytics.conversations.summary:Conversations started in this period: ${count}:count:. Busiest day: ${day}:day: (${most}:most:).`;
}

/** Who replied in the period, AI against the team, in one sentence. */
export function repliesSummary(ai: number, team: number, language: AppLanguage): string {
  if (ai + team === 0) {
    return $localize`:@@analytics.replies.summaryNone:Nobody replied to a customer in this period.`;
  }

  const a = formatNumber(language, ai);
  const t = formatNumber(language, team);
  const share = formatNumber(language, ai / (ai + team), {
    style: 'percent',
    maximumFractionDigits: 0,
  });

  return $localize`:@@analytics.replies.summary:Replies to customers in this period. AI: ${a}:ai:, team: ${t}:team:. The AI wrote ${share}:share: of them.`;
}

/** "106 AI replies failed" with every Arabic plural form. */
export function failedRepliesTitle(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@overview.attention.aiFailed.zero:No AI replies failed`,
    one: $localize`:@@overview.attention.aiFailed.one:${n}:count: AI reply failed`,
    two: $localize`:@@overview.attention.aiFailed.two:${n}:count: AI replies failed`,
    few: $localize`:@@overview.attention.aiFailed.few:${n}:count: AI replies failed`,
    many: $localize`:@@overview.attention.aiFailed.many:${n}:count: AI replies failed`,
    other: $localize`:@@overview.attention.aiFailed.other:${n}:count: AI replies failed`,
  });
}

/** "3 WhatsApp messages were not delivered". */
export function failedDeliveriesTitle(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@overview.attention.whatsapp.zero:Every WhatsApp message was delivered`,
    one: $localize`:@@overview.attention.whatsapp.one:${n}:count: WhatsApp message was not delivered`,
    two: $localize`:@@overview.attention.whatsapp.two:${n}:count: WhatsApp messages were not delivered`,
    few: $localize`:@@overview.attention.whatsapp.few:${n}:count: WhatsApp messages were not delivered`,
    many: $localize`:@@overview.attention.whatsapp.many:${n}:count: WhatsApp messages were not delivered`,
    other: $localize`:@@overview.attention.whatsapp.other:${n}:count: WhatsApp messages were not delivered`,
  });
}

/** "7 unread notifications". */
export function unreadTitle(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@overview.attention.unread.zero:No unread notifications`,
    one: $localize`:@@overview.attention.unread.one:${n}:count: unread notification`,
    two: $localize`:@@overview.attention.unread.two:${n}:count: unread notifications`,
    few: $localize`:@@overview.attention.unread.few:${n}:count: unread notifications`,
    many: $localize`:@@overview.attention.unread.many:${n}:count: unread notifications`,
    other: $localize`:@@overview.attention.unread.other:${n}:count: unread notifications`,
  });
}

function total(day: AnalyticsDay): number {
  return day.customerMessages + day.aiMessages + day.humanMessages;
}
