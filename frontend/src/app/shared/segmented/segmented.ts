import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

/**
 * The segmented control (Phase 39.2b): a soft pill track with the selected
 * segment raised on the panel colour, a control-border edge and weight 600
 * (calm-glass.md §6, owner decision Q3: the edge is the 3:1 cue a shadow
 * cannot give, and it survives forced colours as an outline).
 *
 * An attribute component on the consumer's own container, so the options stay
 * the consumer's elements and keep their ids, testids and locators. One look,
 * three kinds of option, each with its native semantics:
 *
 * - **radios** (`label.segmented-option` around a hidden `<input type=radio>`)
 *   for a choice in a form: arrow keys and the checked state are the
 *   browser's, and the whole segment is the label;
 * - **links** (`a.segmented-option` with `aria-current="true"`) when the
 *   choice is an address (the inbox's views, the team's invitation lists).
 *   Never `role="tab"` for state that lives in the URL;
 * - **buttons** (`button.segmented-option` with `aria-pressed`) for a choice
 *   that acts at once (the language before sign-in).
 *
 * Segments are 36px on the 44px track with a fine pointer, and 44px on touch.
 */
@Component({
  selector: '[app-segmented]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'segmented' },
  template: `<ng-content />`,
  styleUrl: './segmented.scss',
})
export class Segmented {}
