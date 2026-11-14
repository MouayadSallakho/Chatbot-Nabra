import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { PRODUCT_NAME } from '@app/core/brand/brand';
import { APP_LANGUAGE } from '@app/core/i18n/language';
import { BrandMark } from './brand-mark';
import { BRAND_NAMES } from './brand-geometry';

interface DrawnName {
  readonly key: 'latin' | 'arabic';
  readonly path: string;
  readonly viewBox: string;
  /** Width in em, so the name keeps its proportions at any size. */
  readonly width: number;
  /** Height in em: the shared ascent and descent. */
  readonly height: number;
}

/**
 * The box both names share: the deeper ascent and descent of the two fonts, so
 * their baselines line up; its middle sits about half a cap height above the
 * baseline, which is where the mark's middle goes (`align-items: center`).
 */
const ASCENT = Math.max(BRAND_NAMES.latin.ascent, BRAND_NAMES.arabic.ascent);
const DESCENT = Math.max(BRAND_NAMES.latin.descent, BRAND_NAMES.arabic.descent);

function drawn(key: 'latin' | 'arabic'): DrawnName {
  const name = BRAND_NAMES[key];
  const height = ASCENT + DESCENT;

  return {
    key,
    path: name.path,
    viewBox: `0 ${-ASCENT} ${name.advance} ${height}`,
    width: name.advance / 1000,
    height: height / 1000,
  };
}

/**
 * The lockup (brand.md): the mark, then "Nabra" in Geist 600, then «نبرة» in
 * IBM Plex Sans Arabic 700, on one line; on Arabic pages the Arabic name comes
 * first. The mark is about 1.2 × the font size tall.
 *
 * The names are outlined paths (brand-geometry.ts), not text: an English page
 * never downloads the Arabic 700 file for its logo, the shapes are exact in
 * forced colours (`currentColor`), and nothing shifts when a font swaps.
 *
 * One named image: `role="img"` with the product's name in the page's
 * language, so a screen reader hears "Nabra" (or «نبرة») once, not three
 * pictures. `--lockup-size` is the font size it is drawn at.
 */
@Component({
  selector: 'app-brand-lockup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [BrandMark],
  host: {
    class: 'brand-lockup',
    role: 'img',
    '[attr.aria-label]': 'name',
    '[class.is-mono]': "tone() === 'mono'",
  },
  template: `
    <app-brand-mark class="lockup-mark" [tone]="tone()" />
    @for (word of names(); track word.key) {
      <svg
        class="lockup-name"
        [class.lockup-name-arabic]="word.key === 'arabic'"
        [attr.viewBox]="word.viewBox"
        [style.inline-size.em]="word.width"
        [style.block-size.em]="word.height"
        aria-hidden="true"
        focusable="false"
      >
        <path [attr.d]="word.path" />
      </svg>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 0.5em;
      color: var(--color-text);
      font-size: var(--lockup-size, 1.125rem);
      line-height: 1;
      direction: inherit;
    }

    .lockup-mark {
      --brand-mark-size: 1.4em;

      margin-inline-end: 0.1em;
    }

    .lockup-name {
      overflow: visible;
      fill: currentcolor;
    }

    @media (forced-colors: active) {
      .lockup-name {
        fill: CanvasText;
      }
    }
  `,
})
export class BrandLockup {
  private readonly language = inject(APP_LANGUAGE);

  readonly tone = input<'colour' | 'mono'>('colour');

  /** Which name leads; the page's language unless a caller (the design lab) says. */
  readonly lead = input<'en' | 'ar' | null>(null);

  protected readonly name = PRODUCT_NAME;

  /** Latin then Arabic; Arabic first on Arabic pages. */
  protected readonly names = computed<readonly DrawnName[]>(() =>
    (this.lead() ?? this.language) === 'ar'
      ? [drawn('arabic'), drawn('latin')]
      : [drawn('latin'), drawn('arabic')],
  );
}
