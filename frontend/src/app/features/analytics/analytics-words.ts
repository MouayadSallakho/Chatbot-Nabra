import type { AppLanguage } from '@app/core/i18n/language';
import { formatDate, formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import type { ChartPoint, ChartSeries } from '@app/shared/charts/chart-model';
import type { AnalyticsDashboard, AnalyticsDay, AnalyticsRange } from './analytics-api';

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

/** The days a dashboard returned, as chart points: UTC days, the last one marked if it is still running. */
export function dailyPoints(
  dashboard: AnalyticsDashboard,
  language: AppLanguage,
): readonly ChartPoint[] {
  const today = dashboard.period.to.slice(0, 10);
  const stillRunning = !/T00:00:00(\.0+)?(Z|\+00:00)$/.test(dashboard.period.to);

  return dashboard.dailyActivity.map((day) => ({
    key: day.date,
    label: utcDay(day.date, language, 'dayMonth'),
    longLabel: utcDay(day.date, language, 'longDate'),
    partial: stillRunning && day.date === today,
    values: dayValues(day),
  }));
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
 * A `yyyy-MM-dd` UTC day in words.
 *
 * Parsed as UTC deliberately: the buckets are UTC days, and letting the browser
 * read a bare date in local time would shift every label by a day for half the
 * world.
 */
export function utcDay(
  date: string,
  language: AppLanguage,
  style: 'dayMonth' | 'longDate' | 'date' = 'dayMonth',
): string {
  const [year, month, day] = date.split('-').map(Number);

  if (!year || !month || !day) {
    return date;
  }

  return formatDate(language, new Date(Date.UTC(year, month - 1, day)), style, 'UTC');
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

/** "12 – 18 October 2026, in UTC; today until 19:27." */
export function periodSentence(dashboard: AnalyticsDashboard, language: AppLanguage): string {
  const first = dashboard.dailyActivity[0]?.date ?? dashboard.period.from.slice(0, 10);
  const last = dashboard.dailyActivity.at(-1)?.date ?? dashboard.period.to.slice(0, 10);
  const from = utcDay(first, language, 'date');
  const to = utcDay(last, language, 'date');
  const until = formatDate(language, dashboard.period.to, 'time', 'UTC');

  return $localize`:@@analytics.period.sentence:${from}:from: – ${to}:to:, counted in UTC days. Today is counted until ${until}:time: UTC.`;
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
  const day = utcDay(busiest.date, language, 'longDate');
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
  const day = utcDay(busiest.date, language, 'longDate');
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
