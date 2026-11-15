import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleFormat, formatNumber } from '@app/core/i18n/locale-format';
import type { AnalyticsChannel } from '@app/features/analytics/analytics-api';
import { channelWord } from '@app/shared/channel/channel-badge';
import type { AppLanguage } from '@app/core/i18n/language';
import { pluralMessage } from '@app/core/i18n/plural';
import { channelShares } from '../overview-words';

/** "412 conversations" with every Arabic plural form. */
function conversationCount(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@overview.channels.count.zero:${n}:count: conversations`,
    one: $localize`:@@overview.channels.count.one:${n}:count: conversation`,
    two: $localize`:@@overview.channels.count.two:${n}:count: conversations`,
    few: $localize`:@@overview.channels.count.few:${n}:count: conversations`,
    many: $localize`:@@overview.channels.count.many:${n}:count: conversations`,
    other: $localize`:@@overview.channels.count.other:${n}:count: conversations`,
  });
}

/** The order the channel hues are handed out in: emerald, sage, then the ramp. */
const HUES = ['current', 'second', 'third', 'fourth'] as const;

/**
 * "Channels" (board Overview): where the period's conversations started, as
 * one split bar and a legend that says each share in words and figures.
 *
 * The bar draws what the legend says, so it is hidden from assistive
 * technology. Shares are the server's counts over their sum. A period in which
 * no conversation started says so instead of drawing an empty bar.
 */
@Component({
  selector: 'app-channels-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'bento-tile channels-tile',
    '[attr.aria-busy]': 'loading()',
    'data-testid': 'tile-channels',
  },
  template: `
    <h2 class="tile-title" i18n="@@analytics.channels.heading">Channels</h2>
    @if (loading()) {
      <span class="skeleton split-skeleton" aria-hidden="true"></span>
      <span class="skeleton legend-skeleton" aria-hidden="true"></span>
    } @else if (parts().length === 0) {
      <p class="tile-quiet" data-testid="channels-none" i18n="@@analytics.channels.none">
        No conversation started on any channel in this period.
      </p>
    } @else {
      <div class="split" aria-hidden="true">
        @for (part of parts(); track part.channel) {
          <span [class]="'split-part is-' + part.hue" [style.flex-grow]="part.count"></span>
        }
      </div>
      <ul class="legend">
        @for (part of parts(); track part.channel) {
          <li [attr.data-testid]="'channel-' + part.channel">
            <span [class]="'legend-swatch is-' + part.hue" aria-hidden="true"></span>
            <span>{{ part.word }}</span>
            <span class="legend-share">{{ part.percent }}</span>
            <span class="legend-count">{{ part.conversations }}</span>
          </li>
        }
      </ul>
    }
  `,
  styleUrl: './channels-tile.scss',
})
export class ChannelsTile {
  private readonly locale = inject(LocaleFormat);

  readonly loading = input(false);
  readonly channels = input<readonly AnalyticsChannel[]>([]);

  protected readonly parts = computed(() =>
    channelShares(this.channels()).map((share, index) => ({
      ...share,
      hue: HUES[Math.min(index, HUES.length - 1)],
      word: channelWord(share.channel),
      percent: this.locale.percent(share.share, 0),
      conversations: conversationCount(share.count, this.locale.language),
    })),
  );
}
