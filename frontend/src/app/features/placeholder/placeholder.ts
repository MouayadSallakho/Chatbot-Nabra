import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { HOME_PATH } from '@app/layout/navigation';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
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
 * the route table is the only place it is written. Today every area is built and
 * no navigation entry reaches this; the development-only route
 * `/design-lab-unbuilt-area` shows it with a sample name for review.
 */
@Component({
  selector: 'app-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PageHeader, EmptyState, Icon],
  template: `
    <div class="page">
      <app-page-header [heading]="area()" [description]="description()" />

      <div class="page-section">
        <div class="card">
          <app-empty-state icon="blueprint" [heading]="stateHeading" [description]="stateText">
            <a stateActions class="btn btn-primary" [routerLink]="'/' + home">
              <app-icon name="arrow-start" [strokeWidth]="1.75" />
              <span i18n="@@placeholder.back">Back to overview</span>
            </a>
          </app-empty-state>
        </div>
      </div>
    </div>
  `,
})
export class Placeholder {
  /** The product area this route stands in for. Bound from route data. */
  readonly area = input($localize`:@@placeholder.defaultArea:This area`);

  protected readonly home = HOME_PATH;

  protected readonly description = computed(
    () =>
      $localize`:@@placeholder.description:${this.area()}:area: is part of the planned product and is not available in this build.`,
  );

  protected readonly stateHeading = $localize`:@@placeholder.state.title:Not built yet`;
  protected readonly stateText = $localize`:@@placeholder.state.description:This area has no screens in this build. It is planned for a later release, and everything else in Nabra works as usual.`;
}
