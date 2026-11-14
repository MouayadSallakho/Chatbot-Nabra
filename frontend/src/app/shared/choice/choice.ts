import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

/**
 * The choice-control family (Phase 39.2b; Phase 38 notes 3 and 4).
 *
 * One family for the checkbox, the radio and the switch: a visible 20px box
 * (a 40×24 track for the switch) inside a 44px target, and the label is part
 * of the target. Each is an attribute component on the consumer's own
 * `<label>`, wrapping the consumer's own native `<input>`:
 *
 * ```html
 * <label app-check data-testid="role-option-x">
 *   <input type="checkbox" formControlName="roles" />
 *   Support agent
 * </label>
 * ```
 *
 * So the input keeps its `formControlName`, `id`, `name`, `data-testid` and
 * every e2e locator, and keyboard and screen-reader behaviour are the native
 * control's: Space toggles, arrow keys move between radios, the checked state
 * is announced. No ControlValueAccessor is involved (plan 39.0, decision 2).
 * A switch is a checkbox with `role="switch"`, written by the consumer on the
 * input, so it is announced as on or off.
 *
 * The drawing lives in choice.scss, scoped to this family's classes. There is
 * no global `input[type=checkbox]` rule any more, and so no `.switch` escape
 * class: a checkbox outside the family keeps the browser's own look.
 */
const TEMPLATE = `<ng-content select="input" /><span class="choice-label"><ng-content /></span>`;

@Component({
  selector: 'label[app-check]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'choice choice-check' },
  template: TEMPLATE,
  styleUrl: './choice.scss',
})
export class Check {}

@Component({
  selector: 'label[app-radio]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'choice choice-radio' },
  template: TEMPLATE,
  styleUrl: './choice.scss',
})
export class Radio {}

@Component({
  selector: 'label[app-switch]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'choice choice-switch' },
  template: TEMPLATE,
  styleUrl: './choice.scss',
})
export class Switch {}
