import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { Meter } from '@app/shared/charts/meter';
import { PlanEntitlement } from '../plan-usage-api';
import {
  entitlementDescription,
  entitlementLabel,
  quotaConsequence,
  quotaState,
} from '../plan-words';

/**
 * One limit of the plan (Phase 36.10): its name, its state as a word with a
 * marker of its own, the numbers in tabular figures, a meter, and what the
 * state means for the next one.
 *
 * <strong>Presentational.</strong> It draws the entitlement it is given and
 * calls nothing, so the plan page draws the server's figures and the Design
 * Lab can show every state with labelled sample figures.
 *
 * <strong>Unlimited is said, not drawn.</strong> No meter at all: a bar needs
 * something to be a proportion of, and one drawn against nothing implies a
 * limit somewhere to the right.
 */
@Component({
  selector: 'app-quota-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Meter],
  host: { class: 'quota', '[attr.data-state]': 'entitlement().state' },
  template: `
    <div class="quota-head">
      <h3 class="quota-label" [id]="id() + '-label'">{{ label() }}</h3>
      @if (entitlement().unlimited) {
        <span class="badge badge-plain" [attr.data-testid]="'state-' + entitlement().entitlement">{{
          unlimitedWord
        }}</span>
      } @else {
        <span
          class="badge"
          [class]="state().badge"
          [attr.data-testid]="'state-' + entitlement().entitlement"
          >{{ state().word }}</span
        >
      }
    </div>

    <p class="quota-figure" [attr.data-testid]="'figure-' + entitlement().entitlement">
      {{ figure() }}
    </p>

    @if (!entitlement().unlimited && entitlement().limit !== null) {
      <app-meter
        [value]="entitlement().used"
        [max]="entitlement().limit!"
        [state]="state().meter"
        [label]="label()"
        [valueText]="figure() + '. ' + state().word"
      />
      <p class="quota-meta">
        @if (percent(); as used) {
          <span>{{ used }}</span>
        }
        @if (remaining(); as left) {
          <span>{{ left }}</span>
        }
      </p>
    }

    @if (entitlement().unlimited) {
      <p class="quota-meta" i18n="@@plan.noLimit">No limit on this plan.</p>
    }

    @if (period(); as window) {
      <p class="quota-meta" [attr.data-testid]="'period-' + entitlement().entitlement">
        {{ window }}
      </p>
    }

    @if (consequence(); as sentence) {
      <p class="quota-consequence" [attr.data-testid]="'consequence-' + entitlement().entitlement">
        {{ sentence }}
      </p>
    }

    <p class="quota-description">{{ description() }}</p>
  `,
})
export class QuotaRow {
  private readonly locale = inject(LocaleFormat);

  readonly entitlement = input.required<PlanEntitlement>();

  protected readonly unlimitedWord = $localize`:@@plan.unlimited:Unlimited`;

  protected readonly id = computed(() => `quota-${this.entitlement().entitlement}`);
  protected readonly label = computed(() => entitlementLabel(this.entitlement().entitlement));
  protected readonly description = computed(() =>
    entitlementDescription(this.entitlement().entitlement),
  );
  protected readonly state = computed(() => quotaState(this.entitlement().state));

  protected readonly monthly = computed(() => this.entitlement().period !== null);

  protected readonly figure = computed(() => {
    const e = this.entitlement();
    const used = this.locale.number(e.used);

    if (e.unlimited || e.limit === null) {
      return $localize`:@@plan.figure.unlimited:${used}:used: in use`;
    }

    const limit = this.locale.number(e.limit);

    return $localize`:@@plan.figure.limited:${used}:used: of ${limit}:limit:`;
  });

  protected readonly percent = computed(() => {
    const value = this.entitlement().percentUsed;

    if (value === null) {
      return null;
    }

    const percent = this.locale.percent(value / 100, 0);

    return $localize`:@@plan.percentUsed:${percent}:percent: used`;
  });

  protected readonly remaining = computed(() => {
    const value = this.entitlement().remaining;

    if (value === null) {
      return null;
    }

    const left = this.locale.number(value);

    return $localize`:@@plan.remaining:${left}:remaining: left`;
  });

  /**
   * The month a monthly limit covers. The window is half-open, so the last
   * instant belongs to it and the boundary does not: shown as the last day.
   */
  protected readonly period = computed(() => {
    const period = this.entitlement().period;

    if (!period) {
      return null;
    }

    const from = new Date(period.from);
    const to = new Date(new Date(period.to).getTime() - 1);
    const reset = new Date(period.to);
    const first = this.locale.date(from, 'dayMonth', 'UTC');
    const last = this.locale.date(to, 'dayMonth', 'UTC');
    const resets = this.locale.date(reset, 'dayMonth', 'UTC');

    return $localize`:@@plan.period:Counted ${first}:from: to ${last}:to: (UTC); starts again on ${resets}:reset:.`;
  });

  protected readonly consequence = computed(() =>
    this.entitlement().unlimited
      ? null
      : quotaConsequence(this.entitlement().state, this.monthly()),
  );
}
