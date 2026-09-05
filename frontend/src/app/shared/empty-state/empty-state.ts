import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';

/**
 * "There is nothing here", said well.
 *
 * Part of the design system rather than something each feature invents, because
 * empty is the first state every list is in and the one most likely to be left
 * as a blank rectangle. A reader who sees nothing cannot tell whether the data
 * is loading, failed, or genuinely absent -- and only one of those is worth
 * waiting for.
 *
 * No illustration. A large graphic makes an ordinary "no results" feel like an
 * error, adds weight to the bundle, and is the first thing in any interface to
 * look dated.
 */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="state" [class.state-error]="tone() === 'error'">
      <span class="state-icon">
        <app-icon [name]="icon()" [strokeWidth]="1.5" />
      </span>

      <p class="state-title">{{ heading() }}</p>

      @if (description()) {
        <p class="state-description">{{ description() }}</p>
      }

      <div class="state-actions">
        <ng-content select="[stateActions]" />
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .state-actions:empty {
      display: none;
    }
  `,
})
export class EmptyState {
  readonly heading = input.required<string>();

  readonly description = input('');

  readonly icon = input<IconName>('inbox');

  /**
   * Whether this is an absence or a failure.
   *
   * Two tones rather than a colour input: "error" is a meaning, and letting call
   * sites pass a colour is how a design system stops being one.
   */
  readonly tone = input<'neutral' | 'error'>('neutral');
}
