import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ICON_PATHS, IconName } from './icon-paths';

/**
 * Draws one glyph from the product's icon set.
 *
 * Presentation is fixed here rather than per use site: the viewBox, the stroke
 * width, the caps and the joins are the same for every icon in the product, so
 * an inconsistent icon is not something a reviewer has to catch.
 *
 * ICONS ARE DECORATIVE BY DEFAULT. `aria-hidden` is always on and the element is
 * removed from the accessibility tree, because in every current use the glyph
 * sits beside a real text label -- and an icon announced next to the label it
 * illustrates just makes a screen reader say everything twice. Where an icon is
 * the only content of a control, the accessible name belongs on the control
 * itself, which is where assistive technology looks for it.
 */
@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './icon.scss',
  template: `
    <svg
      class="icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      [attr.stroke-width]="strokeWidth()"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      @for (segment of paths(); track segment) {
        <path [attr.d]="segment" />
      }
    </svg>
  `,
})
export class Icon {
  /** Which glyph to draw. */
  readonly name = input.required<IconName>();

  /**
   * Slightly heavier strokes for the small sizes.
   *
   * A 1.5px stroke that reads as crisp at 20px turns into a grey smear at 14px,
   * so the few places that render an icon small can ask for more weight rather
   * than living with it.
   */
  readonly strokeWidth = input(1.5);

  protected readonly paths = computed<readonly string[]>(() => ICON_PATHS[this.name()]);
}
