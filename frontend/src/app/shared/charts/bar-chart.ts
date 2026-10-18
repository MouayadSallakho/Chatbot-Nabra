import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { APP_LANGUAGE, directionOf } from '@app/core/i18n/language';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { ChartFrame } from './chart-frame';
import { measureWidth } from './chart-measure';
import { ChartBar, ChartTone, roundedBar } from './chart-model';

/** A bar's thickness; thin, so the value and the words carry the reading. */
const BAR = 12;

/**
 * Categories side by side, as horizontal bars: channels, agents, kinds of
 * tokens.
 *
 * One series, so one colour and no legend: the title says what is counted.
 * Every bar carries its value at its tip in text, so nothing hides behind a
 * hover, and the label sits on the inline start where a reader starts. Bars
 * grow from the inline start, so in Arabic they grow leftwards. Labels are
 * `dir="auto"`: an agent's name is the workspace's own text, in either script.
 *
 * Read the shape of a one-bar chart as a warning sign: a caller with a single
 * category should say it in a sentence instead (dataviz anti-pattern list).
 */
@Component({
  selector: 'app-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChartFrame],
  host: { class: 'chart' },
  template: `
    <app-chart-frame [heading]="heading()" [level]="level()" [summary]="summary()">
      <ul chartFigure class="bar-chart" data-testid="bar-chart">
        @for (row of rows(); track row.key) {
          <li class="bar-chart-row">
            <span class="bar-chart-label" dir="auto">{{ row.label }}</span>
            <span class="bar-chart-track" #track>
              <svg
                [attr.width]="trackWidth()"
                [attr.height]="barHeight"
                aria-hidden="true"
                focusable="false"
              >
                @if (row.d) {
                  <path [attr.class]="'chart-mark chart-tone-' + tone()" [attr.d]="row.d" />
                }
              </svg>
            </span>
            <span class="bar-chart-value">
              {{ row.value }}
              @if (row.note) {
                <span class="text-muted">{{ row.note }}</span>
              }
            </span>
          </li>
        }
      </ul>

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
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.key) {
              <tr>
                <th scope="row" dir="auto">{{ row.label }}</th>
                <td class="numeric">
                  {{ row.value }}
                  @if (row.note) {
                    <span class="text-muted">{{ row.note }}</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </app-chart-frame>
  `,
})
export class BarChart {
  readonly heading = input.required<string>();
  readonly level = input<2 | 3 | 4>(3);
  readonly summary = input('');
  readonly bars = input.required<readonly ChartBar[]>();
  readonly tone = input<ChartTone>('neutral');

  /** The table's column headings. */
  readonly categoryLabel = input('');
  readonly valueLabel = input('');

  private readonly format = inject(LocaleFormat);
  private readonly rtl = directionOf(inject(APP_LANGUAGE)) === 'rtl';
  private readonly track = viewChild<ElementRef<HTMLElement>>('track');

  protected readonly barHeight = BAR;
  protected readonly trackWidth = signal(240);

  protected readonly rows = computed(() => {
    const bars = this.bars();
    const max = Math.max(0, ...bars.map((bar) => bar.value));
    const width = this.trackWidth();

    return bars.map((bar) => {
      const length = max === 0 ? 0 : Math.max(2, (bar.value / max) * width);
      const x = this.rtl ? width - length : 0;

      return {
        key: bar.key,
        label: bar.label,
        note: bar.note ?? '',
        value: this.format.number(bar.value),
        d: bar.value > 0 ? roundedBar(x, 0, length, BAR, this.rtl ? 'left' : 'right') : '',
      };
    });
  });

  constructor() {
    measureWidth(() => this.track()?.nativeElement, this.trackWidth);
  }
}
