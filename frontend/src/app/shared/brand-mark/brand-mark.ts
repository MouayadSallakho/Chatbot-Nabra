import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { BRAND_MARK } from './brand-geometry';

/**
 * The Nabra mark (brand.md, direction A): two overlapping circles, the AI in
 * emerald and the team in gold, and their overlap -- the lens -- in
 * emerald-900: two voices in one tone, which is what نبرة ("tone of voice")
 * says. On dark grounds the circles are emerald-100 and gold-500 and the lens
 * emerald-300, through the `--brand-*` tokens.
 *
 * ONE COLOUR. `tone="mono"`, and forced colours: the two circle outlines and
 * the filled lens, in `currentColor` (brand.md).
 *
 * NEVER MIRRORED, ROTATED OR GRADIENT. The mark is a logo, not a direction: it
 * renders the same in RTL, and nothing here reads `--direction-flip`.
 *
 * Its box is 140 × 120: `--brand-mark-size` sets the width and the height
 * follows. Decorative by default (the name beside it carries it); pass `label`
 * where the mark stands alone, and it becomes a named image.
 *
 * The same geometry is in brand-geometry.ts and in the boot state of
 * index.html; keep them in step. The favicons and app icons follow in 39.8a.
 */
@Component({
  selector: 'app-brand-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.is-mono]': "tone() === 'mono'" },
  template: `
    <svg
      class="brand-mark-svg"
      [attr.viewBox]="mark.viewBox"
      focusable="false"
      [attr.role]="label() ? 'img' : null"
      [attr.aria-label]="label() || null"
      [attr.aria-hidden]="label() ? null : 'true'"
    >
      <circle class="mark-ai" [attr.cx]="mark.ai.cx" [attr.cy]="mark.ai.cy" [attr.r]="mark.ai.r" />
      <circle
        class="mark-team"
        [attr.cx]="mark.team.cx"
        [attr.cy]="mark.team.cy"
        [attr.r]="mark.team.r"
      />
      <path class="mark-lens" [attr.d]="mark.lens" />
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      inline-size: var(--brand-mark-size, 1.75rem);
      block-size: calc(var(--brand-mark-size, 1.75rem) * 6 / 7);
    }

    .brand-mark-svg {
      inline-size: 100%;
      block-size: 100%;
      overflow: visible;
    }

    .mark-ai {
      fill: var(--brand-ai);
    }

    .mark-team {
      fill: var(--brand-team);
    }

    .mark-lens {
      fill: var(--brand-lens);
    }

    /* One colour: the circles as outlines, the lens filled. */
    :host(.is-mono) circle {
      fill: none;
      stroke: currentcolor;
      stroke-width: 6;
    }

    :host(.is-mono) .mark-lens {
      fill: currentcolor;
    }

    @media (forced-colors: active) {
      circle {
        fill: none;
        stroke: CanvasText;
        stroke-width: 6;
      }

      .mark-lens {
        fill: CanvasText;
      }
    }
  `,
})
export class BrandMark {
  /** `colour` draws the three tones; `mono` draws it in the current colour. */
  readonly tone = input<'colour' | 'mono'>('colour');

  /** An accessible name, for the rare place the mark stands without its name. */
  readonly label = input('');

  protected readonly mark = BRAND_MARK;
}
