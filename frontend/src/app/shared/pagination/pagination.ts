import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  ElementRef,
  afterRenderEffect,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { PluralForms, pluralMessage } from '@app/core/i18n/plural';
import { Icon } from '@app/shared/icon/icon';

/** What a paged list holds. Each kind has its own counted phrase and landmark name. */
export type PagedItems =
  'aiAgents' | 'conversations' | 'customers' | 'documents' | 'notifications' | 'reports';

interface PagedItemsWords {
  /** "of 57 customers", whole, for each plural category. */
  readonly ofTotal: (n: string) => PluralForms;
  /** The navigation landmark's name, so two paged lists on a page differ. */
  readonly label: () => string;
}

/*
 * Whole phrases, never a number glued to a noun. In Arabic the noun's form
 * depends on the count -- عميل واحد, عميلَين, 5 عملاء, 57 عميلًا, 100 عميل -- so
 * each list's phrase is a message of its own with all six categories, and the
 * English source carries the four that English does not need with its `other`
 * wording (localisation-arabic-rtl.md, 3.10).
 */
const WORDS: Readonly<Record<PagedItems, PagedItemsWords>> = {
  aiAgents: {
    ofTotal: (n) => ({
      zero: $localize`:@@pager.aiAgents.zero:of ${n}:count: AI agents`,
      one: $localize`:@@pager.aiAgents.one:of ${n}:count: AI agent`,
      two: $localize`:@@pager.aiAgents.two:of ${n}:count: AI agents`,
      few: $localize`:@@pager.aiAgents.few:of ${n}:count: AI agents`,
      many: $localize`:@@pager.aiAgents.many:of ${n}:count: AI agents`,
      other: $localize`:@@pager.aiAgents.other:of ${n}:count: AI agents`,
    }),
    label: () => $localize`:@@pager.aiAgents.label:AI agent pages`,
  },
  conversations: {
    ofTotal: (n) => ({
      zero: $localize`:@@pager.conversations.zero:of ${n}:count: conversations`,
      one: $localize`:@@pager.conversations.one:of ${n}:count: conversation`,
      two: $localize`:@@pager.conversations.two:of ${n}:count: conversations`,
      few: $localize`:@@pager.conversations.few:of ${n}:count: conversations`,
      many: $localize`:@@pager.conversations.many:of ${n}:count: conversations`,
      other: $localize`:@@pager.conversations.other:of ${n}:count: conversations`,
    }),
    label: () => $localize`:@@pager.conversations.label:Conversation pages`,
  },
  customers: {
    ofTotal: (n) => ({
      zero: $localize`:@@pager.customers.zero:of ${n}:count: customers`,
      one: $localize`:@@pager.customers.one:of ${n}:count: customer`,
      two: $localize`:@@pager.customers.two:of ${n}:count: customers`,
      few: $localize`:@@pager.customers.few:of ${n}:count: customers`,
      many: $localize`:@@pager.customers.many:of ${n}:count: customers`,
      other: $localize`:@@pager.customers.other:of ${n}:count: customers`,
    }),
    label: () => $localize`:@@pager.customers.label:Customer pages`,
  },
  documents: {
    ofTotal: (n) => ({
      zero: $localize`:@@pager.documents.zero:of ${n}:count: documents`,
      one: $localize`:@@pager.documents.one:of ${n}:count: document`,
      two: $localize`:@@pager.documents.two:of ${n}:count: documents`,
      few: $localize`:@@pager.documents.few:of ${n}:count: documents`,
      many: $localize`:@@pager.documents.many:of ${n}:count: documents`,
      other: $localize`:@@pager.documents.other:of ${n}:count: documents`,
    }),
    label: () => $localize`:@@pager.documents.label:Knowledge pages`,
  },
  notifications: {
    ofTotal: (n) => ({
      zero: $localize`:@@pager.notifications.zero:of ${n}:count: notifications`,
      one: $localize`:@@pager.notifications.one:of ${n}:count: notification`,
      two: $localize`:@@pager.notifications.two:of ${n}:count: notifications`,
      few: $localize`:@@pager.notifications.few:of ${n}:count: notifications`,
      many: $localize`:@@pager.notifications.many:of ${n}:count: notifications`,
      other: $localize`:@@pager.notifications.other:of ${n}:count: notifications`,
    }),
    label: () => $localize`:@@pager.notifications.label:Notification pages`,
  },
  reports: {
    ofTotal: (n) => ({
      zero: $localize`:@@pager.reports.zero:of ${n}:count: reports`,
      one: $localize`:@@pager.reports.one:of ${n}:count: report`,
      two: $localize`:@@pager.reports.two:of ${n}:count: reports`,
      few: $localize`:@@pager.reports.few:of ${n}:count: reports`,
      many: $localize`:@@pager.reports.many:of ${n}:count: reports`,
      other: $localize`:@@pager.reports.other:of ${n}:count: reports`,
    }),
    label: () => $localize`:@@pager.reports.label:Generated report pages`,
  },
};

/**
 * The one pagination control, for every server-paged list in the product.
 *
 * It shows the range on screen and the total ("21–40 of 57 customers", in
 * Arabic "21–40 من 57 عميلًا"), and Previous / Next with the current page
 * between them. It never pages on its own: it reports the page the reader asked
 * for, and the list loads it from the server -- nothing here filters or slices
 * data it was not sent.
 *
 * The range is a left-to-right island in both languages: a bare "21–40" inside
 * an Arabic sentence would otherwise be reordered by the bidi algorithm.
 *
 * The summary is a polite live region, so a screen reader hears which rows are
 * now showing after a page change without focus moving anywhere.
 *
 * Focus stays on the button that was pressed. While the page loads the buttons
 * say they are unavailable (`aria-disabled`) and ignore presses rather than
 * being disabled, which would drop focus to the page body (Phase 36.4). At
 * either end of the list the pressed button does become disabled, and focus
 * moves to the other one -- the next meaningful place.
 */
@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    @if (total() > 0) {
      <nav class="pager" [attr.aria-label]="label()">
        <p class="pager-summary" aria-live="polite" data-testid="pager-summary">
          <bdi dir="ltr">{{ range() }}</bdi> {{ ofTotal() }}
        </p>

        @if (pageCount() > 1) {
          <div class="pager-controls">
            <button
              type="button"
              class="btn btn-secondary btn-sm"
              [disabled]="page() <= 1"
              [attr.aria-disabled]="busy() || null"
              (click)="turn(page() - 1)"
            >
              <app-icon name="chevron-start" [strokeWidth]="1.75" />
              <span i18n="@@pager.previous">Previous</span>
            </button>
            <span>{{ pageOf() }}</span>
            <button
              type="button"
              class="btn btn-secondary btn-sm"
              [disabled]="page() >= pageCount()"
              [attr.aria-disabled]="busy() || null"
              (click)="turn(page() + 1)"
            >
              <span i18n="@@pager.next">Next</span>
              <app-icon name="chevron-end" [strokeWidth]="1.75" />
            </button>
          </div>
        }
      </nav>
    }
  `,
  styles: `
    :host {
      display: block;
    }
  `,
})
export class Pagination {
  private readonly format = inject(LocaleFormat);

  /** The page on screen, from 1. */
  readonly page = input.required<number>();

  readonly pageSize = input.required<number>();

  /** Every row the server holds for this list, not just this page's. */
  readonly total = input.required<number>();

  /** What the list holds; chooses the counted phrase and the landmark's name. */
  readonly items = input.required<PagedItems>();

  /** A page is loading: both buttons wait rather than queueing requests. */
  readonly busy = input(false);

  readonly pageChange = output<number>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);

  /**
   * The page a button was pressed on, while focus may need a new home once the
   * new page has settled; null otherwise.
   */
  private pressedOn: number | null = null;

  protected readonly label = computed(() => WORDS[this.items()].label());

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.total() / Math.max(1, this.pageSize()))),
  );

  protected readonly first = computed(() =>
    Math.min(this.total(), (this.page() - 1) * this.pageSize() + 1),
  );

  protected readonly last = computed(() => Math.min(this.total(), this.page() * this.pageSize()));

  protected readonly range = computed(
    () => `${this.format.number(this.first())}–${this.format.number(this.last())}`,
  );

  protected readonly ofTotal = computed(() =>
    pluralMessage(
      this.format.language,
      this.total(),
      WORDS[this.items()].ofTotal(this.format.number(this.total())),
    ),
  );

  constructor() {
    afterRenderEffect(() => {
      // Acts once the new page is in place and nothing is loading.
      const page = this.page();

      if (this.busy() || this.pressedOn === null || this.pressedOn === page) {
        return;
      }

      this.pressedOn = null;

      const focused = this.document.activeElement;
      const lost =
        !focused || focused === this.document.body || (focused as HTMLButtonElement).disabled;

      if (lost) {
        this.host.nativeElement
          .querySelector<HTMLButtonElement>('.pager-controls button:not(:disabled)')
          ?.focus();
      }
    });
  }

  protected turn(target: number): void {
    if (this.busy()) {
      return;
    }

    this.pressedOn = this.page();
    this.pageChange.emit(target);
  }

  protected readonly pageOf = computed(() => {
    const page = this.format.number(this.page());
    const count = this.format.number(this.pageCount());

    return $localize`:@@pager.pageOf:Page ${page}:page: of ${count}:count:`;
  });
}
