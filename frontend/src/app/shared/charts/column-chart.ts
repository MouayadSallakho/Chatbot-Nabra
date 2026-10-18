import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ChartFrame } from './chart-frame';
import { ChartPoint, ChartSeries, ChartTone, roundedBar } from './chart-model';
import { TimeSeriesChart } from './time-series-chart';

/** Thickest a column may be; the band's leftover is air (dataviz mark spec). */
const MAX_COLUMN = 24;

/** The surface gap between stacked segments. */
const GAP = 2;

interface Segment {
  readonly key: string;
  readonly tone: ChartTone;
  readonly d: string;
}

/**
 * Columns over time, stacked by series: messages per day, by who wrote them.
 *
 * The first series sits on the baseline. Segments are kept apart by a 2px gap
 * of the surface rather than an outline, the top of each column is rounded
 * and its foot square. A day still running (today, so far) is drawn lighter
 * and says so in the readout and the table. A day with nothing on it draws
 * nothing, rather than a stub that would read as "a little".
 */
@Component({
  selector: 'app-column-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ChartFrame],
  host: { class: 'chart' },
  template: `
    <app-chart-frame [heading]="heading()" [level]="level()" [summary]="summary()">
      <div chartFigure class="chart-plot">
        <div
          class="chart-surface"
          tabindex="0"
          role="group"
          [attr.aria-label]="heading()"
          [attr.aria-describedby]="hintId"
          (keydown)="onKeydown($event)"
          (blur)="onBlur()"
          (pointermove)="onPointerMove($event)"
          (pointerleave)="onPointerLeave()"
          data-testid="chart-surface"
        >
          <svg
            class="chart-svg"
            [attr.width]="width()"
            [attr.height]="svgHeight()"
            aria-hidden="true"
            focusable="false"
          >
            @for (line of gridLines(); track line.tick) {
              <line
                class="chart-grid-line"
                [class.is-baseline]="line.tick === 0"
                [attr.x1]="line.x1"
                [attr.x2]="line.x2"
                [attr.y1]="line.y"
                [attr.y2]="line.y"
              />
              <text
                class="chart-tick"
                [attr.x]="line.labelX"
                [attr.y]="line.y"
                [attr.text-anchor]="tickAnchor"
                dominant-baseline="middle"
              >
                {{ line.label }}
              </text>
            }
            @if (activeBand(); as bandBox) {
              <rect
                class="chart-active-band"
                [attr.x]="bandBox.x"
                [attr.y]="0"
                [attr.width]="bandBox.width"
                [attr.height]="baseline()"
              />
            }
            @for (column of columns(); track column.key) {
              <g class="chart-column" [class.is-partial]="column.partial">
                @for (segment of column.segments; track segment.key) {
                  <path
                    [attr.class]="'chart-mark chart-tone-' + segment.tone"
                    [attr.d]="segment.d"
                  />
                }
              </g>
            }
            @for (day of dayLabels(); track day.key) {
              <text
                class="chart-tick"
                [attr.x]="day.x"
                [attr.y]="baseline() + 18"
                text-anchor="middle"
              >
                {{ day.label }}
              </text>
            }
          </svg>
        </div>

        @if (readout(); as view) {
          <div class="chart-readout" [style.inset-inline-start.px]="view.inset" aria-hidden="true">
            <p class="chart-readout-title">{{ view.title }}</p>
            @for (row of view.rows; track row.key) {
              <p class="chart-readout-row">
                <span [class]="'chart-key chart-tone-' + row.tone"></span>
                <strong class="chart-readout-value">{{ row.value }}</strong>
                <span>{{ row.label }}</span>
              </p>
            }
          </div>
        }

        <p class="visually-hidden" [id]="hintId">{{ words.keyboardHint }}</p>
        <p class="visually-hidden" aria-live="polite">{{ spoken() }}</p>
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
              @for (series of series(); track series.key) {
                <th scope="col" class="numeric">{{ series.label }}</th>
              }
              @if (series().length > 1) {
                <th scope="col" class="numeric">{{ words.total }}</th>
              }
              @for (extra of tableExtras(); track extra.key) {
                <th scope="col" class="numeric">{{ extra.label }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (point of points(); track point.key) {
              <tr>
                <th scope="row">
                  {{ point.longLabel }}
                  @if (point.partial) {
                    <span class="text-muted">({{ words.soFar }})</span>
                  }
                </th>
                @for (series of series(); track series.key) {
                  <td class="numeric">{{ count(point, series.key) }}</td>
                }
                @if (series().length > 1) {
                  <td class="numeric">{{ format.number(total(point)) }}</td>
                }
                @for (extra of tableExtras(); track extra.key) {
                  <td class="numeric">{{ count(point, extra.key) }}</td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>

      @if (series().length > 1) {
        <ul class="chart-legend" data-testid="chart-legend">
          @for (item of totals(); track item.key) {
            <li>
              <span [class]="'chart-swatch chart-tone-' + item.tone" aria-hidden="true"></span>
              <span>{{ item.label }}</span>
              <span class="chart-legend-value">{{ item.total }}</span>
            </li>
          }
        </ul>
      }
      @if (hasPartial()) {
        <p class="chart-note" data-testid="chart-partial">{{ words.partialColumns }}</p>
      }
    </app-chart-frame>
  `,
})
export class ColumnChart extends TimeSeriesChart {
  /**
   * Columns only the table carries: counts for the same days that are not part
   * of the stack (conversations started, beside messages), so every daily figure
   * the page draws elsewhere -- a sparkline, say -- is in a table too.
   */
  readonly tableExtras = input<readonly ChartSeries[]>([]);

  protected readonly columns = computed(() => {
    const series = this.series();
    const { max } = this.scale();
    const barWidth = Math.max(2, Math.min(MAX_COLUMN, this.band() * 0.62));

    return this.points().map((point, index) => {
      const left = this.center(index) - barWidth / 2;
      const x = this.rtl ? this.mirror(left + barWidth) : left;
      const present = series.filter((s) => (point.values[s.key] ?? 0) > 0);
      const top = present.at(-1)?.key;
      const segments: Segment[] = [];
      let reached = 0;

      for (const s of present) {
        const value = point.values[s.key] ?? 0;
        const yTop = this.y(reached + value, max);
        const yBottom = this.y(reached, max);
        // Every segment above the first leaves the surface gap under it.
        const gap = segments.length > 0 && yBottom - yTop > GAP + 1 ? GAP : 0;

        segments.push({
          key: s.key,
          tone: s.tone,
          d: roundedBar(x, yTop, barWidth, yBottom - yTop - gap, 'top', s.key === top ? 4 : 0),
        });
        reached += value;
      }

      return { key: point.key, partial: point.partial === true, segments };
    });
  });

  protected peak(point: ChartPoint): number {
    return this.total(point);
  }
}
