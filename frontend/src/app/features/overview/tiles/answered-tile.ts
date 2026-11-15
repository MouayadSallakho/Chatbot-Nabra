import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { answeredBefore, answeredCounts, answeredShare } from '../overview-words';

/** The ring's circle: r = 50 in a 120 box, so its length is 2πr. */
const CIRCUMFERENCE = 2 * Math.PI * 50;

/**
 * "Answered by the AI" (board Overview; Phase 39.1, BA39-1; owner decision Q7).
 *
 * The share of the period's conversations in which the AI replied and no person
 * replied or took over, out of the conversations started: both counts are the
 * server's, and the ring only draws their ratio. The figure in the middle and
 * the two lines under it say everything the ring shows, so the drawing is
 * hidden from assistive technology. A period with no conversation has no share,
 * and says so instead of drawing 0%.
 */
@Component({
  selector: 'app-answered-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'bento-tile answered-tile',
    '[attr.aria-busy]': 'loading()',
    'data-testid': 'tile-answered',
  },
  template: `
    <h2 class="tile-title" i18n="@@overview.answered.heading">Answered by the AI</h2>
    @if (loading()) {
      <span class="skeleton ring-skeleton" aria-hidden="true"></span>
      <span class="skeleton skeleton-text answered-skeleton" aria-hidden="true"></span>
      <span class="skeleton skeleton-text answered-skeleton" aria-hidden="true"></span>
    } @else {
      <div class="ring">
        <svg viewBox="0 0 120 120" aria-hidden="true" focusable="false">
          <circle class="ring-track" cx="60" cy="60" r="50" />
          @if (arc(); as length) {
            <circle
              class="ring-arc"
              cx="60"
              cy="60"
              r="50"
              [attr.stroke-dasharray]="length + ' ' + circumference"
              transform="rotate(-90 60 60)"
            />
          }
        </svg>
        @if (percent(); as value) {
          <span class="ring-value" data-testid="answered-share">{{ value }}</span>
        }
      </div>
      @if (percent()) {
        <p class="answered-counts" data-testid="answered-counts">{{ counts() }}</p>
        <p class="answered-note" i18n="@@overview.answered.note">
          Conversations the AI answered with nobody stepping in.
        </p>
        @if (before(); as line) {
          <p class="answered-before" data-testid="answered-before">{{ line }}</p>
        }
      } @else {
        <p class="answered-note" data-testid="answered-none" i18n="@@overview.answered.none">
          No conversation started in the last 7 days.
        </p>
      }
    }
  `,
  styleUrl: './answered-tile.scss',
})
export class AnsweredTile {
  private readonly locale = inject(LocaleFormat);

  readonly loading = input(false);
  readonly answered = input(0);
  readonly started = input(0);

  /** The previous period's two counts, when the comparison was read. */
  readonly previousAnswered = input<number | null>(null);
  readonly previousStarted = input<number | null>(null);

  /** Whether the workspace had conversations before this period. */
  readonly earlierData = input(false);

  protected readonly circumference = CIRCUMFERENCE.toFixed(2);

  private readonly share = computed(() => answeredShare(this.answered(), this.started()));

  protected readonly percent = computed(() => {
    const share = this.share();

    return share === null ? '' : this.locale.percent(share, 0);
  });

  /** The arc's drawn length, or nothing for a share of zero (no dot of a cap). */
  protected readonly arc = computed(() => {
    const share = this.share();

    return share === null || share === 0 ? null : (share * CIRCUMFERENCE).toFixed(2);
  });

  protected readonly counts = computed(() =>
    answeredCounts(this.answered(), this.started(), this.locale.language),
  );

  protected readonly before = computed(() => {
    const answered = this.previousAnswered();
    const started = this.previousStarted();

    return answered === null || started === null
      ? ''
      : answeredBefore(answered, started, this.earlierData(), this.locale.language);
  });
}
