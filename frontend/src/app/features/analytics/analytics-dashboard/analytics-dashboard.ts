import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';
import {
  ANALYTICS_RANGES,
  AnalyticsApi,
  AnalyticsDashboard,
  AnalyticsRange,
  DEFAULT_ANALYTICS_RANGE,
} from '../analytics-api';
import {
  comparisonSentence,
  conversationsSummary,
  conversationActivity,
  dailyPoints,
  dataSinceSentence,
  type FigureChange,
  localDay,
  messagesSummary,
  periodSentence,
  rangeWord,
  repliesSummary,
  senderSeries,
  summaryChanges,
  type SummaryKey,
} from '../analytics-words';
import { ProviderUsageCard } from '../provider-usage/provider-usage';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { LocalNumberPipe } from '@app/core/i18n/locale-pipes';
import { AppLanguage } from '@app/core/i18n/language';
import { channelWord } from '@app/shared/channel/channel-badge';
import { AreaChart } from '@app/shared/charts/area-chart';
import { BarChart } from '@app/shared/charts/bar-chart';
import { ChartBar, ChartPart } from '@app/shared/charts/chart-model';
import { ColumnChart } from '@app/shared/charts/column-chart';
import { StackedBar } from '@app/shared/charts/stacked-bar';
import { Icon } from '@app/shared/icon/icon';
import { ScrollRegion } from '@app/shared/scroll-region/scroll-region';
import { VoiceSplit } from '@app/shared/voice/voice-split';
import { FirstReplyCard } from '@app/features/analytics/first-reply-card';
import { Segmented } from '@app/shared/segmented/segmented';

/**
 * The workspace analytics dashboard.
 *
 * <strong>Every number on this page is a count of something that happened.</strong>
 * There is no sample data, no demo mode and no placeholder chart: a workspace
 * with no activity shows zeroes and says so. A dashboard that invents a plausible
 * line is worse than an empty one, because somebody will make a decision from it.
 *
 * <strong>Voices keep their colours.</strong> Lagoon is the AI and Ember the
 * team in every series here; customers are neutral, and a single series is
 * neutral ink. Every colour is repeated in words: a legend, a summary sentence,
 * and a table behind each chart.
 *
 * <strong>Partial data says so.</strong> The last day of a period is today, and
 * today is not over: it is drawn lighter and labelled "so far", and so is each
 * figure's change against the previous period. A refresh that fails keeps the
 * figures already on screen and says how old they are, rather than blanking the
 * page or pretending they are new.
 *
 * <strong>Days are the workspace's.</strong> Every day is a local day of the
 * workspace's time zone, and the page says which zone the response used
 * (`period.timeZone`, Phase 37.6), never a zone of its own.
 *
 * <strong>The comparison rides on the same read</strong> (`compare=previous`):
 * one request, as before, with the previous period's totals in it.
 *
 * <strong>Provider usage is internal accounting, and lives in its own
 * component.</strong> See {@link ProviderUsageCard}.
 *
 * <strong>Nothing polls.</strong> Analytics is not realtime, and a page that
 * refreshed itself would move numbers under a reader mid-sentence. Refresh is a
 * button; the period lives in the address (`?period=7d`), so a shared link
 * opens the same view.
 */
@Component({
  selector: 'app-analytics-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageHeader,
    EmptyState,
    ProviderUsageCard,
    FirstReplyCard,
    ScrollRegion,
    LocalNumberPipe,
    Icon,
    VoiceSplit,
    ColumnChart,
    AreaChart,
    BarChart,
    StackedBar,
    Segmented,
  ],
  templateUrl: './analytics-dashboard.html',
  styleUrl: './analytics-dashboard.scss',
})
export class AnalyticsDashboardPage {
  private readonly locale = inject(LocaleFormat);

  private readonly api = inject(AnalyticsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Whether these numbers are the reader's to see at all. */
  protected readonly canView = computed(() => this.permissions.has('analytics.view'));

  /**
   * Column names shared with the channel chart's labels and the sender series,
   * written once so each is one message in both languages.
   */
  protected readonly columns = {
    conversations: $localize`:@@analytics.table.conversations:Conversations`,
    customers: $localize`:@@analytics.series.customers:Customers`,
  };

  protected readonly ranges = ANALYTICS_RANGES.map((option) => ({
    value: option.value,
    label: rangeWord(option.value),
  }));

  protected readonly range = signal<AnalyticsRange>(initialRange(this.route));

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly dashboard = signal<AnalyticsDashboard | null>(null);

  /** When the figures on screen were read; shown only if a later read failed. */
  private readonly readAt = signal<Date | null>(null);

  /** A refresh that failed while earlier figures stay on screen. */
  protected readonly staleSince = computed(() => {
    const at = this.readAt();

    return this.failure() !== null && this.dashboard() !== null && at !== null
      ? this.locale.date(at, 'time')
      : null;
  });

  /**
   * Whether the workspace did anything at all in this period.
   *
   * Conversations and messages both, because a period can contain replies in a
   * conversation that started before it.
   */
  protected readonly hasActivity = computed(() => {
    const summary = this.dashboard()?.summary;

    if (!summary) {
      return false;
    }

    return (
      summary.conversationsStarted +
        summary.customerMessages +
        summary.aiMessages +
        summary.humanMessages >
      0
    );
  });

  protected readonly period = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? periodSentence(dashboard, this.locale.language) : '';
  });

  /** "Compared with …", the previous period's days. */
  protected readonly comparison = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? comparisonSentence(dashboard, this.locale.language) : null;
  });

  /** "Data since …", or that no conversation has started yet. */
  protected readonly since = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? dataSinceSentence(dashboard.firstActivityOn, this.locale.language) : '';
  });

  /** A workspace that has never had a conversation: its own empty state. */
  protected readonly neverActive = computed(() => this.dashboard()?.firstActivityOn === null);

  private readonly changes = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? summaryChanges(dashboard, this.locale.language) : null;
  });

  /** The four period figures, each with its change when the comparison came back. */
  protected readonly figures = computed(() => {
    const summary = this.dashboard()?.summary;
    const changes = this.changes();

    if (!summary) {
      return [];
    }

    const figure = (key: SummaryKey, testid: string, label: string, value: number) => ({
      key,
      testid,
      label,
      value,
      change: (changes?.[key] ?? null) as FigureChange | null,
    });

    return [
      figure(
        'conversations',
        'summary-conversations',
        $localize`:@@analytics.figure.conversations:Conversations started`,
        summary.conversationsStarted,
      ),
      figure(
        'customer',
        'summary-customer',
        $localize`:@@analytics.figure.customer:Customer messages`,
        summary.customerMessages,
      ),
      figure('ai', 'summary-ai', $localize`:@@analytics.figure.ai:AI replies`, summary.aiMessages),
      figure(
        'human',
        'summary-human',
        $localize`:@@analytics.figure.team:Replies from the team`,
        summary.humanMessages,
      ),
    ];
  });

  protected readonly series = senderSeries();

  protected readonly activity = computed(() =>
    conversationActivity(this.dashboard(), this.locale.language),
  );

  protected readonly points = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? dailyPoints(dashboard, this.locale.language) : [];
  });

  protected readonly messagesSummary = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? messagesSummary(dashboard, this.locale.language) : '';
  });

  protected readonly conversationsSummary = computed(() => {
    const dashboard = this.dashboard();

    return dashboard ? conversationsSummary(dashboard, this.locale.language) : '';
  });

  protected readonly replyParts = computed<readonly ChartPart[]>(() => {
    const summary = this.dashboard()?.summary;

    return [
      {
        key: 'ai',
        label: $localize`:@@analytics.series.ai:AI`,
        value: summary?.aiMessages ?? 0,
        tone: 'ai',
      },
      {
        key: 'human',
        label: $localize`:@@analytics.series.team:Team`,
        value: summary?.humanMessages ?? 0,
        tone: 'human',
      },
    ];
  });

  protected readonly repliesSummary = computed(() => {
    const summary = this.dashboard()?.summary;

    return repliesSummary(
      summary?.aiMessages ?? 0,
      summary?.humanMessages ?? 0,
      this.locale.language,
    );
  });

  /** Channels as bars, each with its share of the period's conversations. */
  protected readonly channelBars = computed<readonly ChartBar[]>(() => {
    const dashboard = this.dashboard();
    const total = dashboard?.summary.conversationsStarted ?? 0;

    return (dashboard?.channels ?? []).map((channel) => ({
      key: channel.channel,
      label: channelWord(channel.channel),
      value: channel.conversationsStarted,
      note: total === 0 ? '' : this.locale.percent(channel.conversationsStarted / total),
    }));
  });

  protected readonly channelsSummary = computed(() => {
    const bars = this.channelBars();
    const busiest = [...bars].sort((a, b) => b.value - a.value)[0];

    if (!busiest) {
      return '';
    }

    const name = busiest.label;
    const share = busiest.note ?? '';

    return $localize`:@@analytics.channels.summary:Where conversations started. Most came through ${name}:channel: (${share}:share:).`;
  });

  /** With one channel there is nothing to compare: a sentence, not a one-bar chart. */
  protected readonly singleChannel = computed(() => {
    const bars = this.channelBars();

    if (bars.length !== 1) {
      return '';
    }

    const name = bars[0].label;
    const count = this.locale.number(bars[0].value);

    return $localize`:@@analytics.channels.single:Every conversation in this period started on ${name}:channel:: ${count}:count:.`;
  });

  /** How AI reply attempts ended: the expected outcome in ink, failures in danger. */
  protected readonly outcomeParts = computed<readonly ChartPart[]>(() => {
    const operations = this.dashboard()?.aiOperations;
    const parts: ChartPart[] = [
      {
        key: 'completed',
        label: $localize`:@@analytics.outcome.completed:Completed`,
        value: operations?.completed ?? 0,
        tone: 'neutral',
      },
      {
        key: 'failed',
        label: $localize`:@@analytics.outcome.failed:Failed`,
        value: operations?.failed ?? 0,
        tone: 'danger',
      },
    ];

    if ((operations?.inProgress ?? 0) > 0) {
      parts.push({
        key: 'running',
        label: $localize`:@@analytics.outcome.running:Still running`,
        value: operations?.inProgress ?? 0,
        tone: 'muted',
      });
    }

    return parts;
  });

  protected readonly outcomeSummary = computed(() => {
    const operations = this.dashboard()?.aiOperations;
    const attempts = this.locale.number(operations?.attempts ?? 0);
    const failed = this.locale.number(operations?.failed ?? 0);

    return $localize`:@@analytics.outcome.summary:AI reply attempts in this period: ${attempts}:attempts:. Failed: ${failed}:failed:. Counted from what the runtime recorded, not estimated.`;
  });

  /** WhatsApp delivery, in the order a message travels. */
  protected readonly deliveryRows = computed(() => {
    const delivery = this.dashboard()?.whatsappDelivery;

    if (!delivery) {
      return [];
    }

    return [
      {
        key: 'pending',
        label: $localize`:@@analytics.delivery.pending:Waiting to send`,
        value: delivery.pending,
      },
      {
        key: 'sending',
        label: $localize`:@@analytics.delivery.sending:Sending`,
        value: delivery.sending,
      },
      { key: 'sent', label: $localize`:@@analytics.delivery.sent:Sent`, value: delivery.sent },
      {
        key: 'delivered',
        label: $localize`:@@analytics.delivery.delivered:Delivered`,
        value: delivery.delivered,
      },
      { key: 'read', label: $localize`:@@analytics.delivery.read:Read`, value: delivery.read },
      {
        key: 'failed',
        label: $localize`:@@analytics.delivery.failed:Not delivered`,
        value: delivery.failed,
      },
      {
        key: 'unknown',
        label: $localize`:@@analytics.delivery.unknown:Unknown`,
        value: delivery.unknown,
      },
    ];
  });

  /** Said once when numbers arrive, for somebody who cannot see the cards change. */
  protected readonly announcement = computed(() => {
    const dashboard = this.dashboard();

    if (!dashboard) {
      return '';
    }

    const range = rangeWord(dashboard.period.range as AnalyticsRange) || dashboard.period.range;
    const n = (value: number) => this.locale.number(value);

    return $localize`:@@analytics.announcement:${range}:range:: conversations started ${n(dashboard.summary.conversationsStarted)}:conversations:, customer messages ${n(dashboard.summary.customerMessages)}:customer:, AI replies ${n(dashboard.summary.aiMessages)}:ai:, replies from the team ${n(dashboard.summary.humanMessages)}:team:.`;
  });

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  constructor() {
    void this.load();
  }

  /** Switches the period. Re-reads, because the numbers are all period-scoped. */
  protected async select(range: AnalyticsRange): Promise<void> {
    if (range === this.range() || this.refreshing()) {
      return;
    }

    this.range.set(range);

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { period: range === DEFAULT_ANALYTICS_RANGE ? null : range },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });

    await this.load();
  }

  /** Re-reads the same period. */
  protected async refresh(): Promise<void> {
    if (this.refreshing()) {
      return;
    }

    await this.load();
  }

  protected async load(): Promise<void> {
    const tenantId = this.tenantId();

    if (!tenantId) {
      this.failure.set({
        kind: 'unknown',
        status: 0,
        message: $localize`:@@analytics.noWorkspace:No workspace is selected.`,
      });
      this.loading.set(false);

      return;
    }

    if (!this.canView()) {
      // Nothing worth asking for. The template explains the refusal rather than
      // reporting an error from a request that was certain to be refused.
      this.loading.set(false);

      return;
    }

    // The first load has nothing to show yet; a refresh keeps the previous
    // figures on screen, dimmed, rather than blanking them.
    this.refreshing.set(true);
    this.failure.set(null);

    try {
      this.dashboard.set(
        await firstValueFrom(this.api.dashboard(tenantId, this.range(), { compare: true })),
      );
      this.readAt.set(new Date());
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
      this.refreshing.set(false);
    }
  }
}

/** The period from the address, or the default; anything else in `?period=` is ignored. */
function initialRange(route: ActivatedRoute): AnalyticsRange {
  const requested = route.snapshot.queryParamMap.get('period');

  return ANALYTICS_RANGES.some((option) => option.value === requested)
    ? (requested as AnalyticsRange)
    : DEFAULT_ANALYTICS_RANGE;
}

/**
 * A `yyyy-MM-dd` day of the workspace's zone as a short label, written from its
 * parts. Kept as the page's export for its callers; the words live in
 * `analytics-words`.
 */
export function formatDay(date: string, language: AppLanguage = 'en'): string {
  return localDay(date, language, 'dayMonth');
}
