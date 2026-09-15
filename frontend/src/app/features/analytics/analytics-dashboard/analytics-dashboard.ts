import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
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

/** One bar in the activity chart, already scaled. */
interface ActivityBar {
  readonly date: string;
  readonly label: string;
  readonly total: number;
  readonly customer: number;
  readonly ai: number;
  readonly human: number;

  /** Height as a percentage of the busiest day, 0–100. */
  readonly height: number;
}

/**
 * The workspace analytics dashboard.
 *
 * <strong>Every number on this page is a count of something that happened.</strong>
 * There is no sample data, no demo mode and no placeholder chart: a workspace
 * with no activity shows zeroes and says so. A dashboard that invents a plausible
 * line is worse than an empty one, because somebody will make a decision from it.
 *
 * <strong>It is not billing.</strong> No cost, no token count, no plan and no
 * estimate appears here, because the platform does not hold those numbers. When
 * provider usage accounting exists it can be added; inventing "$0" in the
 * meantime would be a lie with a currency symbol on it.
 *
 * <strong>The chart is drawn with CSS, and it is not the only representation.</strong>
 * No chart library was added. Every bar is also a row in a table below it, which
 * is what a screen reader reads and what somebody checking a number actually
 * wants. Colour distinguishes the series, and so does the text beside it.
 *
 * <strong>Nothing polls.</strong> Analytics is not realtime, and a page that
 * refreshed itself would move numbers under a reader mid-sentence. Refresh is a
 * button.
 */
@Component({
  selector: 'app-analytics-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, EmptyState],
  templateUrl: './analytics-dashboard.html',
  styleUrl: './analytics-dashboard.scss',
})
export class AnalyticsDashboardPage {
  private readonly api = inject(AnalyticsApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** Whether these numbers are the reader's to see at all. */
  protected readonly canView = computed(() => this.permissions.has('analytics.view'));

  protected readonly ranges = ANALYTICS_RANGES;

  protected readonly range = signal<AnalyticsRange>(DEFAULT_ANALYTICS_RANGE);

  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly dashboard = signal<AnalyticsDashboard | null>(null);

  /** Placeholder cards, so the loading state has the shape of the real one. */
  protected readonly skeletonCards = [0, 1, 2, 3];

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

  /**
   * The daily bars, scaled to the busiest day.
   *
   * Scaled here rather than in the template so the arithmetic is testable and
   * happens once per load instead of once per change detection pass.
   */
  protected readonly bars = computed<readonly ActivityBar[]>(() => {
    const days = this.dashboard()?.dailyActivity ?? [];

    const totals = days.map((day) => day.customerMessages + day.aiMessages + day.humanMessages);

    const busiest = Math.max(0, ...totals);

    return days.map((day, index) => ({
      date: day.date,
      label: formatDay(day.date),
      total: totals[index],
      customer: day.customerMessages,
      ai: day.aiMessages,
      human: day.humanMessages,

      // A day with nothing in it is a floor, not a full-height bar: dividing by
      // a busiest of zero would otherwise produce NaN or 100%.
      height: busiest === 0 ? 0 : Math.round((totals[index] / busiest) * 100),
    }));
  });

  /** The busiest day's total, for the chart's axis label. */
  protected readonly busiestDay = computed(() =>
    Math.max(0, ...this.bars().map((bar) => bar.total)),
  );

  /** Whether any day in the period had anything on it. */
  protected readonly hasDailyActivity = computed(() => this.busiestDay() > 0);

  protected readonly tenantId = computed(() => this.workspace.tenantId() ?? '');

  constructor() {
    void this.load();
  }

  /** The words a reader sees for each channel. */
  protected channelName(channel: string): string {
    switch (channel) {
      case 'WebChat':
        return 'Website chat';
      case 'WhatsApp':
        return 'WhatsApp';
      case 'Instagram':
        return 'Instagram';
      case 'Messenger':
        return 'Messenger';
      default:
        return channel;
    }
  }

  /**
   * One channel's share of the period's conversations, as a percentage.
   *
   * Of conversations rather than of messages, because that is what the bar sits
   * beside. Zero conversations means zero share rather than a division by zero.
   */
  protected channelShare(conversations: number): number {
    const total = this.dashboard()?.summary.conversationsStarted ?? 0;

    return total === 0 ? 0 : Math.round((conversations / total) * 100);
  }

  /** Switches the period. Re-reads, because the numbers are all period-scoped. */
  protected async select(range: AnalyticsRange): Promise<void> {
    if (range === this.range() || this.refreshing()) {
      return;
    }

    this.range.set(range);

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
      this.failure.set({ kind: 'unknown', status: 0, message: 'No workspace is selected.' });
      this.loading.set(false);

      return;
    }

    if (!this.canView()) {
      // Nothing worth asking for. The template explains the refusal rather than
      // reporting an error from a request that was certain to be refused.
      this.loading.set(false);

      return;
    }

    // Two flags, because they mean different things to a reader: the first load
    // has nothing to show yet, while a refresh has the previous numbers on
    // screen and should not blank them.
    this.refreshing.set(true);
    this.failure.set(null);

    try {
      this.dashboard.set(await firstValueFrom(this.api.dashboard(tenantId, this.range())));
    } catch (error) {
      this.failure.set(toApiError(error));
      this.dashboard.set(null);
    } finally {
      this.loading.set(false);
      this.refreshing.set(false);
    }
  }
}

/**
 * A `yyyy-MM-dd` string as a short day label.
 *
 * Parsed as UTC deliberately: the buckets are UTC days, and letting the browser
 * read a bare date in local time would shift every label by a day for half the
 * world.
 */
export function formatDay(date: string): string {
  const [year, month, day] = date.split('-').map(Number);

  if (!year || !month || !day) {
    return date;
  }

  return new Date(Date.UTC(year, month - 1, day)).toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    timeZone: 'UTC',
  });
}
