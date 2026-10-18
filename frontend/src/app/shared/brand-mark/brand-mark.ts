import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** The mark's geometry on a 23-unit grid (viewBox "2 4 23 23"). */
export const BRAND_MARK_PATHS = {
  /** The AI bubble, tail at the lower left, with the Ember bubble cut out. */
  ai: 'M4.4 18.35 3.5 23l4.7-1.6.54-.21A9.5 9.5 0 0 1 17.02 7 8.5 8.5 0 0 0 4.4 18.35Z',
  /** The person bubble, in front, tail at the lower right. */
  human: 'M17 9a7.5 7.5 0 0 1 5.8 12.25l.8 4.25-4.1-1.4A7.5 7.5 0 1 1 17 9Z',
} as const;

/**
 * The Nabra mark: two rounded speech shapes, overlapping -- the AI (Lagoon) and
 * a person on the team (Ember): two voices in one tone, which is what the name
 * نبرة ("tone of voice") says. Since the brand step the shapes no longer stand
 * for the letter "O" of the code name; the geometry is unchanged.
 *
 * DRAWN, NOT MASKED. The Lagoon shape is pre-cut: its path is the AI bubble
 * minus the Ember bubble grown by a 2-unit gap, computed once (Phase 36.2). So
 * the two shapes stay apart on any background and in a single colour, with no
 * `<mask>` or `url(#id)` reference -- which `<base href="/">` breaks in some
 * browsers and which would need a unique id per instance.
 *
 * NEVER MIRRORED. The mark is a logo, not a direction: it renders the same in
 * RTL, and nothing here reads `--direction-flip`.
 *
 * Decorative by default (the wordmark beside it carries the name). Pass
 * `label` where the mark stands alone, and it becomes a named image.
 *
 * The same geometry is in public/nabra-icon.v1.svg (and its .ico) and the
 * index.html loading state; keep the three in step.
 */
@Component({
  selector: 'app-brand-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.is-mono]': "tone() === 'mono'" },
  template: `
    <svg
      class="brand-mark-svg"
      viewBox="2 4 23 23"
      focusable="false"
      [attr.role]="label() ? 'img' : null"
      [attr.aria-label]="label() || null"
      [attr.aria-hidden]="label() ? null : 'true'"
    >
      <path class="voice-ai" [attr.d]="aiPath" />
      <path class="voice-human" [attr.d]="humanPath" />
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      inline-size: var(--brand-mark-size, 1.75rem);
      block-size: var(--brand-mark-size, 1.75rem);
    }

    .brand-mark-svg {
      inline-size: 100%;
      block-size: 100%;
    }

    .voice-ai {
      fill: var(--voice-ai-fg);
    }

    .voice-human {
      fill: var(--voice-human-fg);
    }

    :host(.is-mono) path {
      fill: currentcolor;
    }

    @media (forced-colors: active) {
      path {
        fill: CanvasText;
      }
    }
  `,
})
export class BrandMark {
  /** `voices` draws Lagoon and Ember; `mono` draws both in the current colour. */
  readonly tone = input<'voices' | 'mono'>('voices');

  /** An accessible name, for the rare place the mark stands without a wordmark. */
  readonly label = input('');

  protected readonly aiPath = BRAND_MARK_PATHS.ai;
  protected readonly humanPath = BRAND_MARK_PATHS.human;
}
