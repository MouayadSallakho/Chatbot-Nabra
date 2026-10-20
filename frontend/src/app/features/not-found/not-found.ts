import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HOME_PATH } from '@app/layout/navigation';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';

/**
 * An address that matches nothing.
 *
 * Rendered INSIDE the shell rather than replacing it, deliberately. A mistyped
 * URL is an ordinary event, and someone who hits one still knows where they are
 * and can reach any other page in one click. Throwing away the navigation would
 * turn a small mistake into a dead end.
 *
 * It does not redirect. A silent bounce to the overview leaves the reader
 * wondering whether the link they followed was wrong or the product simply
 * ignored it -- and it hides broken links from whoever needs to fix them.
 *
 * A calm state, not an error: nothing failed, and nothing in the workspace
 * changed. It shows the path that matched nothing, as a left-to-right island,
 * so the reader can see a typo or report the link. Only the path: a query or a
 * fragment is never echoed back, in case a link carried something private.
 */
@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, Icon],
  template: `
    <div class="page">
      <app-page-header [heading]="heading" [description]="description" />

      <div class="page-section">
        <div class="card">
          <div class="state" data-testid="not-found">
            <span class="state-icon">
              <app-icon name="search" [strokeWidth]="1.5" />
            </span>
            <p class="state-title" i18n="@@notFound.state.title">Nothing at this address</p>
            <p class="state-description" i18n="@@notFound.state.description">
              The link may be out of date or mistyped. Nothing in your workspace has changed.
            </p>
            <p class="not-found-address">
              <code dir="ltr">{{ address }}</code>
            </p>
            <div class="state-actions">
              <a class="btn btn-primary" [routerLink]="'/' + home">
                <app-icon name="arrow-start" [strokeWidth]="1.75" />
                <span i18n="@@notFound.back">Back to overview</span>
              </a>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: `
    .not-found-address {
      margin: 0;
      max-inline-size: 100%;
      overflow-wrap: anywhere;
    }

    .not-found-address code {
      padding: var(--gap-hairline) var(--space-2);
      border-radius: var(--radius-badge);
      background-color: var(--surface-inset);
      color: var(--text-secondary);
      font: var(--text-small);
      font-family: var(--font-mono);
    }
  `,
})
export class NotFound {
  protected readonly home = HOME_PATH;

  protected readonly heading = $localize`:@@notFound.heading:Page not found`;
  protected readonly description = $localize`:@@notFound.description:The address you followed does not match any page in Nabra.`;

  /** The path that matched nothing, without its query or fragment. */
  protected readonly address = inject(Router).url.split(/[?#]/)[0];
}
