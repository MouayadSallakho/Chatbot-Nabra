import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Icon } from '@app/shared/icon/icon';

/**
 * The one pagination control, for every server-paged list in the product.
 *
 * It shows the range on screen and the total ("21–40 of 57 customers"), and
 * Previous / Next with the current page between them. It never pages on its
 * own: it reports the page the reader asked for, and the list loads it from the
 * server -- nothing here filters or slices data it was not sent.
 *
 * The summary is a polite live region, so a screen reader hears which rows are
 * now showing after a page change without focus moving anywhere.
 */
@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, DecimalPipe],
  template: `
    @if (total() > 0) {
      <nav class="pager" [attr.aria-label]="label()">
        <p class="pager-summary" aria-live="polite">
          {{ first() | number }}–{{ last() | number }} of {{ total() | number }}
          {{ total() === 1 ? noun() : plural() }}
        </p>

        @if (pageCount() > 1) {
          <div class="pager-controls">
            <button
              type="button"
              class="btn btn-secondary btn-sm"
              [disabled]="page() <= 1 || busy()"
              (click)="pageChange.emit(page() - 1)"
            >
              <app-icon name="chevron-start" [strokeWidth]="1.75" />
              <span>Previous</span>
            </button>
            <span>Page {{ page() | number }} of {{ pageCount() | number }}</span>
            <button
              type="button"
              class="btn btn-secondary btn-sm"
              [disabled]="page() >= pageCount() || busy()"
              (click)="pageChange.emit(page() + 1)"
            >
              <span>Next</span>
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
  /** The page on screen, from 1. */
  readonly page = input.required<number>();

  readonly pageSize = input.required<number>();

  /** Every row the server holds for this list, not just this page's. */
  readonly total = input.required<number>();

  /** What one row is, for the summary: "customer". */
  readonly noun = input.required<string>();

  /** The plural, where adding "s" would be wrong. */
  readonly nounPlural = input('');

  /** The navigation landmark's name, so two paged lists on a page differ. */
  readonly label = input('Pages');

  /** A page is loading: both buttons wait rather than queueing requests. */
  readonly busy = input(false);

  readonly pageChange = output<number>();

  protected readonly plural = computed(() => this.nounPlural() || `${this.noun()}s`);

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.total() / Math.max(1, this.pageSize()))),
  );

  protected readonly first = computed(() =>
    Math.min(this.total(), (this.page() - 1) * this.pageSize() + 1),
  );

  protected readonly last = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
}
