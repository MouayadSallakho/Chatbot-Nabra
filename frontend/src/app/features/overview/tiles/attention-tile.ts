import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleFormat, formatNumber } from '@app/core/i18n/locale-format';
import type { AppLanguage } from '@app/core/i18n/language';
import { pluralMessage } from '@app/core/i18n/plural';
import { Chip, ChipTone } from '@app/shared/chip/chip';
import { Icon } from '@app/shared/icon/icon';

/** How much an item matters, most serious first. `unavailable` is a source that did not load. */
export type AttentionTone = 'critical' | 'warning' | 'info' | 'unavailable';

/** Something the reader should look at, and the one thing to do about it. */
export interface AttentionItem {
  readonly id: string;
  readonly tone: AttentionTone;
  readonly title: string;
  readonly detail: string;

  /** A route to follow, or null when the action is "Try again" in place. */
  readonly link: string | null;

  /** The route's query, when it has one. */
  readonly linkQuery?: Readonly<Record<string, string>>;
  readonly action: string;
}

/**
 * How many items the tile shows before "+N more". The tile is this many rows
 * high in every state, so the bento never moves while the sources answer
 * (Phase 36.12, revision 1); only the reader's own "+N more" makes it taller.
 */
export const ATTENTION_ROWS = 3;

const TONE_WORDS: Readonly<Record<AttentionTone, string>> = {
  critical: $localize`:@@overview.tone.critical:Critical`,
  warning: $localize`:@@overview.tone.warning:Warning`,
  info: $localize`:@@overview.tone.info:For your information`,
  unavailable: $localize`:@@overview.tone.unavailable:Not checked`,
};

/** Each severity's chip: a status word with its own glyph (owner decision Q4). */
const TONE_CHIPS: Readonly<Record<AttentionTone, ChipTone>> = {
  critical: 'danger',
  warning: 'warning',
  info: 'info',
  unavailable: 'neutral',
};

/** "+2 more": a sentence per plural category, so Arabic agrees with the count. */
function moreLabel(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@overview.attention.more.zero:No more`,
    one: $localize`:@@overview.attention.more.one:+${n}:count: more`,
    two: $localize`:@@overview.attention.more.two:+${n}:count: more`,
    few: $localize`:@@overview.attention.more.few:+${n}:count: more`,
    many: $localize`:@@overview.attention.more.many:+${n}:count: more`,
    other: $localize`:@@overview.attention.more.other:+${n}:count: more`,
  });
}

/**
 * "Needs attention": what else needs the reader, most serious first, beside the
 * gold "Needs a person now" tile. A subscription in arrears, a limit running
 * out, AI replies that failed, WhatsApp messages not delivered, unread
 * notifications, and a source that could not be checked, said as such rather
 * than as "all clear".
 *
 * Each row is the new row shape (Phase 38 note 10): the title on its own line,
 * never truncated, and under it the severity as a chip with its word and glyph,
 * then the detail. The whole row is the one action.
 */
@Component({
  selector: 'app-attention-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Chip, Icon],
  host: {
    class: 'bento-tile attention-tile',
    '[class.is-expanded]': 'expanded()',
    '[attr.aria-busy]': 'loading()',
    'data-testid': 'attention-panel',
  },
  template: `
    <h2 class="tile-title" id="attention-heading" i18n="@@overview.attention.heading">
      Needs attention
    </h2>

    @if (loading()) {
      <p class="visually-hidden" role="status" i18n="@@overview.attention.loading">
        Checking what needs attention…
      </p>
      <ul class="attention-list" aria-hidden="true">
        @for (row of placeholders; track row) {
          <li class="attention-row is-placeholder">
            <span class="skeleton attention-skeleton-title"></span>
            <span class="skeleton attention-skeleton-chip"></span>
          </li>
        }
      </ul>
    } @else if (items().length === 0) {
      <div class="attention-clear" data-testid="attention-clear">
        <app-chip tone="success" i18n="@@overview.attention.clear.word">All clear</app-chip>
        <p i18n="@@overview.attention.clear">Nothing needs you right now.</p>
      </div>
    } @else {
      <ul
        class="attention-list"
        id="attention-list"
        aria-labelledby="attention-heading"
        data-testid="attention"
      >
        @for (item of visible(); track item.id) {
          <li [class]="'attention-row is-' + item.tone" [attr.data-testid]="'attention-' + item.id">
            @if (item.link; as link) {
              <a class="attention-target" [routerLink]="link" [queryParams]="item.linkQuery">
                <span class="attention-title">{{ item.title }}</span>
                <span class="attention-meta">
                  <app-chip size="sm" [tone]="chips[item.tone]">{{ words[item.tone] }}</app-chip>
                  <span class="attention-detail">{{ item.detail }}</span>
                </span>
                <span class="attention-action">
                  <span class="visually-hidden-below-md">{{ item.action }}</span>
                  <app-icon class="attention-arrow" name="chevron-end" [strokeWidth]="1.75" />
                </span>
              </a>
            } @else {
              <button type="button" class="attention-target" (click)="retry.emit()">
                <span class="attention-title">{{ item.title }}</span>
                <span class="attention-meta">
                  <app-chip size="sm" [tone]="chips[item.tone]">{{ words[item.tone] }}</app-chip>
                  <span class="attention-detail">{{ item.detail }}</span>
                </span>
                <!-- "Try again" stays visible: the only sign the row is a button. -->
                <span class="attention-action">{{ item.action }}</span>
              </button>
            }
          </li>
        }
      </ul>
    }

    @if (!loading() && hidden() > 0) {
      <button
        type="button"
        class="btn btn-quiet btn-sm attention-more"
        aria-controls="attention-list"
        [attr.aria-expanded]="expanded()"
        (click)="expanded.set(!expanded())"
        data-testid="attention-more"
      >
        @if (expanded()) {
          <span i18n="@@overview.attention.fewer">Show fewer</span>
        } @else {
          {{ moreText() }}
        }
      </button>
    }
  `,
  styleUrl: './attention-tile.scss',
})
export class AttentionTile {
  private readonly locale = inject(LocaleFormat);

  readonly loading = input(false);

  /** Most serious first. */
  readonly items = input<readonly AttentionItem[]>([]);

  /** "Try again" on a source that did not answer. */
  readonly retry = output<void>();

  protected readonly words = TONE_WORDS;
  protected readonly chips = TONE_CHIPS;
  protected readonly placeholders = Array.from({ length: ATTENTION_ROWS }, (_, i) => i);

  /** The reader asked to see every item, not only the first three. */
  protected readonly expanded = signal(false);

  protected readonly visible = computed(() =>
    this.expanded() ? this.items() : this.items().slice(0, ATTENTION_ROWS),
  );

  protected readonly hidden = computed(() => Math.max(0, this.items().length - ATTENTION_ROWS));

  protected readonly moreText = computed(() => moreLabel(this.hidden(), this.locale.language));
}
