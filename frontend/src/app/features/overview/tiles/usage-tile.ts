import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import type { PlanEntitlement } from '@app/features/plan-usage/plan-usage-api';
import { quotaState } from '@app/features/plan-usage/plan-words';
import { Meter } from '@app/shared/charts/meter';
import { Chip, ChipTone } from '@app/shared/chip/chip';

/** The state's chip on the deep tile: a status word with its glyph. */
const STATE_TONES: Readonly<Record<string, ChipTone>> = {
  Normal: 'success',
  Warning: 'warning',
  Critical: 'danger',
  Exhausted: 'danger',
};

/**
 * "AI usage this month" (board Overview): the plan's monthly AI provider
 * operations, as the plan-usage read returns them, against the plan's limit.
 *
 * The deep tile: every colour inside is the dark look's, from the tile tokens.
 * A limit gets a meter and the server's state as a chip with its word; a plan
 * with no monthly limit says so and draws no meter, because a bar against no
 * limit would be invented.
 */
@Component({
  selector: 'app-usage-tile',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Meter, Chip],
  host: {
    class: 'bento-tile usage-tile',
    '[attr.aria-busy]': 'loading()',
    'data-testid': 'tile-usage',
  },
  template: `
    <div class="usage-head">
      <h2 class="tile-title" i18n="@@overview.usage.heading">AI usage this month</h2>
      @if (limitText(); as limit) {
        <span class="usage-limit" data-testid="usage-limit">{{ limit }}</span>
      }
    </div>

    @if (loading()) {
      <span class="skeleton usage-skeleton-figure" aria-hidden="true"></span>
      <span class="skeleton usage-skeleton-meter" aria-hidden="true"></span>
    } @else if (failed()) {
      <p class="usage-note" role="alert">{{ unavailable }}</p>
      <button type="button" class="btn btn-secondary btn-sm usage-retry" (click)="retry.emit()">
        <span i18n="@@overview.action.tryAgain">Try again</span>
      </button>
    } @else if (entitlement(); as usage) {
      <p class="usage-figure">
        <span class="usage-value" data-testid="usage-used">{{ used() }}</span>
        <span class="usage-unit" i18n="@@plan.limit.providerOperations"
          >AI provider operations</span
        >
      </p>
      @if (usage.limit !== null && !usage.unlimited) {
        <app-meter
          [value]="usage.used"
          [max]="usage.limit"
          [state]="state().meter"
          [label]="label"
          [valueText]="valueText()"
        />
        <div class="usage-foot">
          <app-chip size="sm" [tone]="tone()" data-testid="usage-state">{{
            state().word
          }}</app-chip>
          <span class="usage-note">{{ figure() }}</span>
        </div>
      }
      <a class="usage-link" routerLink="/plan-usage" i18n="@@overview.action.planUsage"
        >View plan & usage</a
      >
    }
  `,
  styleUrl: './usage-tile.scss',
})
export class UsageTile {
  private readonly locale = inject(LocaleFormat);

  readonly loading = input(false);
  readonly failed = input(false);

  /** The `monthlyProviderOperations` entitlement, or null when the plan read has none. */
  readonly entitlement = input<PlanEntitlement | null>(null);

  readonly retry = output<void>();

  protected readonly label = $localize`:@@plan.limit.providerOperations:AI provider operations`;
  protected readonly unavailable = $localize`:@@overview.attention.planUnavailable:Plan limits could not be checked`;

  protected readonly used = computed(() => this.locale.number(this.entitlement()?.used ?? 0));

  protected readonly state = computed(() => quotaState(this.entitlement()?.state ?? 'Normal'));

  protected readonly tone = computed<ChipTone>(
    () => STATE_TONES[this.entitlement()?.state ?? 'Normal'] ?? 'neutral',
  );

  /** "Plan limit: 200", or "No monthly limit on this plan". Empty while nothing is known. */
  protected readonly limitText = computed(() => {
    const usage = this.entitlement();

    if (!usage || this.loading() || this.failed()) {
      return '';
    }

    if (usage.unlimited || usage.limit === null) {
      return $localize`:@@overview.usage.unlimited:No monthly limit on this plan`;
    }

    const limit = this.locale.number(usage.limit);

    return $localize`:@@overview.usage.limit:Plan limit: ${limit}:limit:`;
  });

  /** "31 of 200", the plan-usage page's own words. */
  protected readonly figure = computed(() => {
    const usage = this.entitlement();
    const used = this.locale.number(usage?.used ?? 0);
    const limit = this.locale.number(usage?.limit ?? 0);

    return $localize`:@@plan.figure.limited:${used}:used: of ${limit}:limit:`;
  });

  protected readonly valueText = computed(() => `${this.figure()}. ${this.state().word}`);
}
