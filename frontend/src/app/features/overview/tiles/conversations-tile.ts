import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { zonePhrase } from '@app/core/i18n/time-zone';
import type { AnalyticsDashboard } from '@app/features/analytics/analytics-api';
import {
  conversationsSummary,
  dailyPoints,
  dataSinceSentence,
  summaryChanges,
} from '@app/features/analytics/analytics-words';
import { AreaChart } from '@app/shared/charts/area-chart';
import type { ChartSeries } from '@app/shared/charts/chart-model';

/**
 * "Conversations" (board Overview, the 2×2 tile): how many started in the last
 * seven days, how that compares with the seven days before (37.6's
 * `compare=previous`), and the days as a line in the current period's emerald.
 *
 * The change is said in words, with a glyph that only repeats them, and no
 * colour says whether a rise is good. The chart keeps its summary sentence and
 * its "View as table". The days are local days of the workspace's zone, and the
 * tile says which zone and how far back the data goes. The board's dashed line
 * for the week before is not drawn: the API returns the previous period's
 * totals, not its days (backend ask BA39-7a-1).
 */
@Component({
  selector: 'app-conversations-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AreaChart],
  host: {
    class: 'bento-tile conversations-tile',
    '[attr.aria-busy]': '!dashboard()',
    'data-testid': 'tile-conversations',
  },
  template: `
    <div class="conv-head">
      <h2 class="tile-title" i18n="@@overview.conversations.heading">Conversations</h2>
      <span class="tile-quiet" i18n="@@overview.conversations.period">Last 7 days</span>
    </div>

    @if (dashboard(); as d) {
      <p class="conv-figure">
        <span class="conv-value" data-testid="week-conversations">{{ total() }}</span>
        @if (change(); as c) {
          <!-- The words carry the direction; the glyph only repeats them. -->
          <span
            class="conv-change"
            [attr.data-direction]="c.direction"
            data-testid="week-conversations-change"
          >
            @if (c.glyph) {
              <span class="conv-change-glyph" aria-hidden="true">{{ c.glyph }}</span>
            }
            <span>{{ c.text }}</span>
          </span>
        }
      </p>
      <app-area-chart
        heading="Conversations started per day"
        i18n-heading="@@analytics.days.conversations"
        categoryLabel="Day"
        i18n-categoryLabel="@@analytics.table.day"
        [summary]="summary()"
        [series]="series"
        [points]="points()"
        [plotHeight]="132"
      />
      <p class="tile-quiet conv-note" data-testid="week-note">{{ note() }}</p>
    } @else {
      <span class="skeleton conv-skeleton-figure" aria-hidden="true"></span>
      <span class="skeleton skeleton-text conv-skeleton-line" aria-hidden="true"></span>
      <span class="skeleton conv-skeleton-plot" aria-hidden="true"></span>
      <span class="skeleton skeleton-text conv-skeleton-line" aria-hidden="true"></span>
    }
  `,
  styleUrl: './conversations-tile.scss',
})
export class ConversationsTile {
  private readonly locale = inject(LocaleFormat);

  /** The seven-day dashboard read with its comparison, or null while it loads. */
  readonly dashboard = input<AnalyticsDashboard | null>(null);

  protected readonly series: readonly ChartSeries[] = [
    {
      key: 'conversations',
      label: $localize`:@@overview.week.conversationsStarted:Conversations started`,
      tone: 'current',
    },
  ];

  protected readonly total = computed(() =>
    this.locale.number(this.dashboard()?.summary.conversationsStarted ?? 0),
  );

  protected readonly change = computed(() => {
    const d = this.dashboard();

    return d ? (summaryChanges(d, this.locale.language)?.conversations ?? null) : null;
  });

  protected readonly points = computed(() => {
    const d = this.dashboard();

    return d ? dailyPoints(d, this.locale.language) : [];
  });

  protected readonly summary = computed(() => {
    const d = this.dashboard();

    return d ? conversationsSummary(d, this.locale.language) : '';
  });

  /** "Whole days in Asia/Riyadh time. Data since Mar 3, 2026." */
  protected readonly note = computed(() => {
    const d = this.dashboard();

    if (!d) {
      return '';
    }

    const zone = zonePhrase(d.period.timeZone);
    const days = $localize`:@@overview.week.zone:Whole days ${zone}:zone:.`;

    return `${days} ${dataSinceSentence(d.firstActivityOn, this.locale.language)}`;
  });
}
