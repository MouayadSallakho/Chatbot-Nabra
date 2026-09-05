import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HOME_PATH } from '@app/layout/navigation';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { PageHeader } from '@app/shared/page-header/page-header';

/**
 * Stands in for a product area that has not been built.
 *
 * ONE COMPONENT FOR ALL OF THEM, reached by every unbuilt route. The alternative
 * -- a component per area -- would mean seven files that differ only in a string
 * and would invite somebody to start "just sketching" a feature inside one of
 * them.
 *
 * It says plainly that the area does not exist yet. That is the point: a
 * navigation entry that leads to a convincing but non-functional screen is worse
 * than one that leads here, because it takes a reader time to work out that
 * nothing they do will have any effect.
 *
 * The area name arrives from route data through `withComponentInputBinding`, so
 * the route table is the only place it is written.
 */
@Component({
  selector: 'app-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState],
  template: `
    <div class="page">
      <app-page-header [heading]="area()" [description]="description()" />

      <div class="page-section">
        <div class="card">
          <app-empty-state
            icon="blueprint"
            heading="Not implemented yet"
            [description]="
              area() +
              ' has no screens in this build. The application shell, design system and navigation are in place, and this area will be built on top of them in a later phase.'
            "
          >
            <a stateActions class="btn btn-secondary btn-sm" [routerLink]="'/' + home">
              Back to overview
            </a>
          </app-empty-state>
        </div>
      </div>
    </div>
  `,
})
export class Placeholder {
  /** The product area this route stands in for. Bound from route data. */
  readonly area = input('This area');

  protected readonly home = HOME_PATH;

  protected description(): string {
    return `${this.area()} is part of the planned product and is not available in this build.`;
  }
}
