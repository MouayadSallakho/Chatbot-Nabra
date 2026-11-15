import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { countMessage } from '@app/core/i18n/plural';
import type { ConversationSummary } from '@app/features/conversations/conversations-api';
import { CustomerLabel } from '@app/features/conversations/customer-label';
import { inboxRow } from '@app/features/conversations/inbox-rows';
import { Chip } from '@app/shared/chip/chip';
import { Icon } from '@app/shared/icon/icon';

/** How many waiting conversations the tile names before "Open the inbox". */
export const NEEDS_PERSON_ROWS = 3;

/**
 * "Needs a person now" (board Overview; Phase 38 note 10).
 *
 * The gold tile: how many conversations wait for somebody on the team, the
 * first three of them, each on two lines (who it is with, then why it waits as
 * a chip with its word), and one action, "Open the inbox". The count and the
 * rows come from one read of the inbox's own needs-a-person view, so they are
 * exactly what the inbox would list, in its order. Nothing is truncated: a
 * long name wraps.
 */
@Component({
  selector: 'app-needs-person-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Chip, CustomerLabel, Icon],
  host: {
    class: 'bento-tile needs-person-tile',
    '[attr.aria-busy]': 'loading()',
    'data-testid': 'tile-needs-person',
  },
  template: `
    <div class="need-head">
      <h2 class="need-title" id="needs-person-heading" i18n="@@overview.needsPerson.heading">
        Needs a person now
      </h2>
      @if (!loading() && !failed()) {
        <span class="need-count" data-testid="needs-person-total">{{ totalText() }}</span>
      }
    </div>

    @if (loading()) {
      <ul class="need-rows" aria-hidden="true">
        @for (row of placeholders; track row) {
          <li class="need-row is-placeholder">
            <span class="skeleton skeleton-text need-skeleton-name"></span>
            <span class="skeleton need-skeleton-chip"></span>
          </li>
        }
      </ul>
    } @else if (failed()) {
      <p class="need-message" role="alert" i18n="@@overview.needsPerson.failed">
        The waiting conversations could not be read.
      </p>
      <button type="button" class="btn btn-secondary btn-sm need-retry" (click)="retry.emit()">
        <span i18n="@@overview.action.tryAgain">Try again</span>
      </button>
    } @else if (rows().length === 0) {
      <p class="need-message" data-testid="needs-person-none">{{ noneText }}</p>
    } @else {
      <ul class="need-rows" aria-labelledby="needs-person-heading">
        @for (row of rows(); track row.conversation.id) {
          <li>
            <a
              class="need-row"
              [routerLink]="['/conversations', row.conversation.id]"
              [attr.data-testid]="'needs-person-row-' + row.conversation.id"
            >
              <span class="need-name">
                <app-customer-label [customer]="row.conversation.customer" />
              </span>
              @if (row.reason; as reason) {
                <app-chip size="sm" [tone]="reason.tone">{{ reason.word }}</app-chip>
              }
            </a>
          </li>
        }
      </ul>
      @if (more(); as label) {
        <p class="need-more" data-testid="needs-person-more">{{ label }}</p>
      }
    }

    <a
      class="btn btn-primary need-open"
      routerLink="/conversations"
      [queryParams]="{ view: 'needs-person' }"
      data-testid="needs-person-open"
    >
      <span i18n="@@overview.needsPerson.open">Open the inbox</span>
      <app-icon class="need-open-arrow" name="chevron-end" [strokeWidth]="1.75" />
    </a>
  `,
  styleUrl: './needs-person-tile.scss',
})
export class NeedsPersonTile {
  private readonly locale = inject(LocaleFormat);

  readonly loading = input(false);
  readonly failed = input(false);

  /** The first page of the needs-a-person view, newest activity first. */
  readonly conversations = input<readonly ConversationSummary[]>([]);

  /** How many conversations the view holds in all. */
  readonly total = input(0);

  readonly retry = output<void>();

  protected readonly placeholders = Array.from({ length: NEEDS_PERSON_ROWS }, (_, i) => i);

  protected readonly rows = computed(() =>
    this.conversations()
      .slice(0, NEEDS_PERSON_ROWS)
      .map((conversation) => inboxRow(conversation, 'needs-person')),
  );

  protected readonly totalText = computed(() => this.locale.number(this.total()));

  protected readonly noneText = $localize`:@@overview.attention.needsPerson.zero:No conversations need a person`;

  /** "and 149 more in the inbox", when the tile names fewer than there are. */
  protected readonly more = computed(() => {
    const hidden = Math.max(0, this.total() - this.rows().length);

    if (hidden === 0) {
      return '';
    }

    const n = this.locale.number(hidden);

    return countMessage(this.locale.language, hidden, {
      zero: '',
      one: $localize`:@@overview.needsPerson.more.one:and ${n}:count: more in the inbox`,
      two: $localize`:@@overview.needsPerson.more.two:and ${n}:count: more in the inbox`,
      few: $localize`:@@overview.needsPerson.more.few:and ${n}:count: more in the inbox`,
      many: $localize`:@@overview.needsPerson.more.many:and ${n}:count: more in the inbox`,
      other: $localize`:@@overview.needsPerson.more.other:and ${n}:count: more in the inbox`,
    });
  });
}
