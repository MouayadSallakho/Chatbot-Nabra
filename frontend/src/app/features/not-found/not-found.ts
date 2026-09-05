import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HOME_PATH } from '@app/layout/navigation';
import { EmptyState } from '@app/shared/empty-state/empty-state';
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
 */
@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState],
  template: `
    <div class="page">
      <app-page-header
        heading="Page not found"
        description="The address you followed does not match any page in this application."
      />

      <div class="page-section">
        <div class="card">
          <app-empty-state
            icon="alert"
            tone="error"
            heading="Nothing at this address"
            description="The link may be out of date, or the page may not have been built yet."
          >
            <a stateActions class="btn btn-primary btn-sm" [routerLink]="'/' + home">
              Back to overview
            </a>
          </app-empty-state>
        </div>
      </div>
    </div>
  `,
})
export class NotFound {
  protected readonly home = HOME_PATH;
}
