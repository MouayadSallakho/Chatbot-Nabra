import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { APP_LANGUAGE, directionOf } from '@app/core/i18n/language';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { ChartFrame } from './chart-frame';
import { measureWidth } from './chart-measure';
import { ChartPart, roundedBar } from './chart-model';

const HEIGHT = 12;
const GAP = 2;

/**
 * The parts of one whole, as a single bar: replies by the AI and by people,
 * how AI reply attempts ended.
 *
 * Parts run from the inline start in the order given, separated by a 2px gap
 * of the surface; only the two outer ends are rounded. The legend under the
 * bar is the reading: every part's word, count and share, so the colours only
 * repeat it. A part with nothing in it keeps its legend line (a zero is a
 * fact) and draws no sliver.
 */
@Component({
  selector: 'app-stacked-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChartFrame],
  host: { class: 'chart' },
  template: `
    <app-chart-frame [heading]="heading()" [level]="level()" [summary]="summary()">
      <div chartFigure class="stacked-bar">
        <svg [attr.width]="width()" [attr.height]="height" aria-hidden="true" focusable="false">
          <rect
            class="stacked-bar-track"
            x="0"
            y="0"
            [attr.width]="width()"
            [attr.height]="height"
            rx="4"
          />
          @for (segment of segments(); track segment.key) {
            <path [attr.class]="'chart-mark chart-tone-' + segment.tone" [attr.d]="segment.d" />
          }
        </svg>
      </div>

      <div chartTable class="table-scroll chart-table">
        <table class="table" data-testid="chart-table">
          <caption class="visually-hidden">
            {{
              heading()
            }}
          </caption>
          <thead>
            <tr>
              <th scope="col">{{ categoryLabel() }}</th>
              <th scope="col" class="numeric">{{ valueLabel() }}</th>
              <th scope="col" class="numeric">%</th>
            </tr>
          </thead>
          <tbody>
            @for (item of legend(); track item.key) {
              <tr>
                <th scope="row">{{ item.label }}</th>
                <td class="numeric">{{ item.value }}</td>
                <td class="numeric">{{ item.share }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <ul class="chart-legend" data-testid="chart-legend">
        @for (item of legend(); track item.key) {
          <li>
            <span [class]="'chart-swatch chart-tone-' + item.tone" aria-hidden="true"></span>
            <span>{{ item.label }}</span>
            <span class="chart-legend-value">{{ item.value }}</span>
            @if (item.share) {
              <span class="text-muted">{{ item.share }}</span>
            }
          </li>
        }
      </ul>
    </app-chart-frame>
  `,
})
export class StackedBar {
  readonly heading = input.required<string>();
  readonly level = input<2 | 3 | 4>(3);
  readonly summary = input('');
  readonly parts = input.required<readonly ChartPart[]>();
  readonly categoryLabel = input('');
  readonly valueLabel = input('');

  private readonly format = inject(LocaleFormat);
  private readonly rtl = directionOf(inject(APP_LANGUAGE)) === 'rtl';
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly height = HEIGHT;
  protected readonly width = signal(480);

  private readonly total = computed(() =>
    this.parts().reduce((sum, part) => sum + Math.max(0, part.value), 0),
  );

  protected readonly segments = computed(() => {
    const total = this.total();
    const width = this.width();
    const present = this.parts().filter((part) => part.value > 0);

    if (total === 0) {
      return [];
    }

    const room = width - GAP * (present.length - 1);
    let start = 0;

    return present.map((part, index) => {
      const length = Math.max(2, (part.value / total) * room);
      const first = index === 0;
      const last = index === present.length - 1;
      const x = this.rtl ? width - start - length : start;
      start += length + GAP;

      // Only the bar's two outer ends are rounded; a lone part is rounded at both.
      const d =
        first && last
          ? `M${x + 4},0H${x + length - 4}Q${x + length},0 ${x + length},4V${HEIGHT - 4}Q${x + length},${HEIGHT} ${x + length - 4},${HEIGHT}H${x + 4}Q${x},${HEIGHT} ${x},${HEIGHT - 4}V4Q${x},0 ${x + 4},0Z`
          : first
            ? roundedBar(x, 0, length, HEIGHT, this.rtl ? 'right' : 'left')
            : last
              ? roundedBar(x, 0, length, HEIGHT, this.rtl ? 'left' : 'right')
              : `M${x},0H${x + length}V${HEIGHT}H${x}Z`;

      return { key: part.key, tone: part.tone, d };
    });
  });

  protected readonly legend = computed(() => {
    const total = this.total();

    return this.parts().map((part) => ({
      key: part.key,
      label: part.label,
      tone: part.tone,
      value: this.format.number(part.value),
      share: total === 0 ? '' : this.format.percent(part.value / total),
    }));
  });

  constructor() {
    measureWidth(() => this.host.nativeElement, this.width);
  }
}
