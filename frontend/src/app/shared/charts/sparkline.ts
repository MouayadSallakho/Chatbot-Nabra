import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { APP_LANGUAGE, directionOf } from '@app/core/i18n/language';
import { ChartTone } from './chart-model';

const WIDTH = 96;
const HEIGHT = 28;
const PAD = 3;

/**
 * A figure's shape over the last days, as a 2px line with no axes.
 *
 * Decorative by contract: it sits beside a number that is printed in full, and
 * the same daily values are in a table nearby, so it is hidden from assistive
 * technology rather than read out as a second, vaguer copy of them. It draws
 * only the values it is given, oldest at the inline start (so it runs right to
 * left in Arabic), scaled from zero so a flat week does not look like a swing.
 */
@Component({
  selector: 'app-sparkline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'sparkline', 'aria-hidden': 'true' },
  template: `
    <svg [attr.width]="width" [attr.height]="height" focusable="false">
      <line
        class="chart-grid-line"
        [attr.x1]="0"
        [attr.x2]="width"
        [attr.y1]="baseline"
        [attr.y2]="baseline"
      />
      @if (path(); as d) {
        <path [attr.class]="'chart-line chart-tone-' + tone()" [attr.d]="d.line" />
        <circle
          [attr.class]="'chart-dot chart-tone-' + tone()"
          [attr.cx]="d.endX"
          [attr.cy]="d.endY"
          r="3"
        />
      }
    </svg>
  `,
})
export class Sparkline {
  readonly values = input.required<readonly number[]>();
  readonly tone = input<ChartTone>('neutral');

  private readonly rtl = directionOf(inject(APP_LANGUAGE)) === 'rtl';

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly baseline = HEIGHT - PAD;

  protected readonly path = computed(() => {
    const values = this.values();

    if (values.length < 2) {
      return null;
    }

    const max = Math.max(1, ...values);
    const step = (WIDTH - PAD * 2) / (values.length - 1);
    const points = values.map((value, index) => {
      const logical = PAD + step * index;

      return {
        x: this.rtl ? WIDTH - logical : logical,
        y: HEIGHT - PAD - (value / max) * (HEIGHT - PAD * 2),
      };
    });
    const last = points[points.length - 1];

    return {
      line: points.map((p, index) => `${index === 0 ? 'M' : 'L'}${p.x},${p.y}`).join(''),
      endX: last.x,
      endY: last.y,
    };
  });
}
