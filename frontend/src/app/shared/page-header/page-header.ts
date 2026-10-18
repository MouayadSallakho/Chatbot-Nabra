import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { NavigationHistory } from '@app/core/navigation/navigation-history';
import { Icon } from '@app/shared/icon/icon';

/**
 * The heading block every page opens with.
 *
 * A component rather than a copied block of markup, because it fixes what the
 * reader learns once and then relies on everywhere:
 *
 * - where the page's `<h1>` is -- exactly one per view, which is what heading
 *   navigation depends on;
 * - where "up" is: a detail page names its parent above the title, never as one
 *   more button among the page's actions;
 * - where the actions are: at the end of the title row, the primary one last.
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  styleUrl: './page-header.scss',
  template: `
    <header class="page-header">
      @if (backLink(); as link) {
        <!--
          A real link to the parent, so it can be opened in a new tab and reads
          as navigation. A plain click returns to the parent exactly as the
          reader left it -- the same page of a list -- when that is where they
          came from.
        -->
        <a class="page-header-back" [attr.href]="link" (click)="goBack($event, link)">
          <app-icon name="arrow-start" [strokeWidth]="1.75" />
          <span
            ><span class="visually-hidden" i18n="@@pageHeader.backTo">Back to </span
            >{{ backLabel() }}</span
          >
        </a>
      }

      <div class="page-header-row">
        <div class="page-header-text">
          <div class="page-header-title">
            @if (level() === 1) {
              <h1 class="text-page-title" [attr.dir]="headingDir()">{{ heading() }}</h1>
            } @else {
              <h2 class="text-page-title" [attr.dir]="headingDir()">{{ heading() }}</h2>
            }
            <ng-content select="[pageStatus]" />
          </div>
          @if (description()) {
            <p class="page-header-description">{{ description() }}</p>
          }
        </div>

        <!--
          Optional trailing actions. Collapsed by CSS when nothing is projected, so
          a page with no actions gets no stray gap.
        -->
        <div class="page-header-actions">
          <ng-content select="[pageActions]" />
        </div>
      </div>
    </header>
  `,
})
export class PageHeader {
  /**
   * The page's title.
   *
   * Named `heading` rather than `title` on purpose: an input called `title`
   * would collide with the native global attribute and quietly give the host
   * element a tooltip.
   */
  readonly heading = input.required<string>();

  /**
   * The heading's level: 1 for a page, 2 for a pane that is not the page right
   * now (the inbox list beside an open conversation, whose heading is the
   * page's). Same size either way; only the document outline changes.
   */
  readonly level = input<1 | 2>(1);

  /**
   * `auto` when the heading is somebody's own text -- a customer's name -- so an
   * Arabic name reads right to left on an English page and the other way
   * round. Left unset for headings the product writes, which follow the page.
   */
  readonly headingDir = input<'auto' | null>(null);

  /** One sentence on what the page is for. Optional. */
  readonly description = input('');

  /** The parent screen's address, for detail and create pages. */
  readonly backLink = input<string | null>(null);

  /** The parent screen's name: "Knowledge", not "Back to knowledge". */
  readonly backLabel = input('');

  private readonly router = inject(Router);
  private readonly history = inject(NavigationHistory);

  protected goBack(event: MouseEvent, link: string): void {
    // A modified click means "somewhere else": a new tab or window. Leave it to
    // the browser, which opens the plain parent address.
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return;
    }

    event.preventDefault();
    void this.router.navigateByUrl(this.history.returnUrl(link));
  }
}
