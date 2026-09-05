import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The heading block every page opens with.
 *
 * A component rather than a copied block of markup, because it fixes one thing
 * that matters beyond appearance: the page's `<h1>`. Exactly one per view, in a
 * predictable place, is what makes heading navigation work for screen reader
 * users -- and it is the first thing to drift when every page writes its own
 * header.
 */
@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './page-header.scss',
  template: `
    <header class="page-header">
      <div class="page-header-text">
        <h1 class="text-page-title">{{ heading() }}</h1>
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

  /** One sentence on what the page is for. Optional. */
  readonly description = input('');
}
