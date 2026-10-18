import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** What a meter's fill says about the amount. */
export type MeterState = 'normal' | 'warning' | 'critical';

/**
 * How much of a known limit is used: "9 of 10 members".
 *
 * A real `meter` role with its minimum, maximum, value and a value text in
 * words, so assistive technology hears the same sentence the screen shows.
 * The fill carries the state (ink, then warning amber, then danger); the
 * track is a lighter step behind it, and the words beside it always say the
 * numbers, so the state never rests on the colour. Only for a limit the API
 * actually returned: an unlimited entitlement has no meter.
 */
@Component({
  selector: 'app-meter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'meter',
    role: 'meter',
    '[attr.aria-valuemin]': '0',
    '[attr.aria-valuemax]': 'max()',
    '[attr.aria-valuenow]': 'clamped()',
    '[attr.aria-valuetext]': 'valueText()',
    '[attr.aria-label]': 'label()',
    '[class.is-warning]': "state() === 'warning'",
    '[class.is-critical]': "state() === 'critical'",
  },
  template: `<span class="meter-fill" [style.inline-size.%]="percent()"></span>`,
})
export class Meter {
  readonly value = input.required<number>();
  readonly max = input.required<number>();
  readonly state = input<MeterState>('normal');

  /** What is measured ("Members"). */
  readonly label = input.required<string>();

  /** The amount in words ("9 of 10 used"), the same sentence the page prints. */
  readonly valueText = input.required<string>();

  protected readonly clamped = computed(() => Math.min(Math.max(0, this.value()), this.max()));

  protected readonly percent = computed(() =>
    this.max() <= 0 ? 0 : Math.round((this.clamped() / this.max()) * 1000) / 10,
  );
}
