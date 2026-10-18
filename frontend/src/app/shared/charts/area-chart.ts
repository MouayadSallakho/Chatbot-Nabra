import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { ChartFrame } from './chart-frame';
import { ChartPoint } from './chart-model';
import { TimeSeriesChart } from './time-series-chart';

/**
 * A daily series as a line over a light wash: conversations started per day.
 *
 * Series are drawn on one value axis and never on two: a second measure of a
 * different size is a second chart. The line is 2px with round joins, the wash
 * the series' colour at about a tenth, and the newest point carries a marker
 * with a ring of the surface so it stays legible where it meets the line. The
 * highlighted day gets a hairline crosshair; the reader aims at a day, never at
 * a 2px line.
 */
@Component({
  selector: 'app-area-chart',
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
            @for (path of paths(); track path.key) {
              <path [attr.class]="'chart-area chart-tone-' + path.tone" [attr.d]="path.area" />
              <path [attr.class]="'chart-line chart-tone-' + path.tone" [attr.d]="path.line" />
            }
            @if (crosshair(); as cross) {
              <line
                class="chart-crosshair"
                [attr.x1]="cross.x"
                [attr.x2]="cross.x"
                [attr.y1]="0"
                [attr.y2]="baseline()"
              />
              @for (dot of cross.dots; track dot.key) {
                <circle
                  [attr.class]="'chart-dot chart-tone-' + dot.tone"
                  [attr.cx]="cross.x"
                  [attr.cy]="dot.y"
                  r="4"
                />
              }
            } @else {
              @for (path of paths(); track path.key) {
                <circle
                  [attr.class]="'chart-dot chart-tone-' + path.tone"
                  [attr.cx]="path.endX"
                  [attr.cy]="path.endY"
                  r="4"
                />
              }
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
                <span [class]="'chart-key is-line chart-tone-' + row.tone"></span>
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
              </tr>
            }
          </tbody>
        </table>
      </div>

      @if (series().length > 1) {
        <ul class="chart-legend">
          @for (item of totals(); track item.key) {
            <li>
              <span
                [class]="'chart-swatch is-line chart-tone-' + item.tone"
                aria-hidden="true"
              ></span>
              <span>{{ item.label }}</span>
              <span class="chart-legend-value">{{ item.total }}</span>
            </li>
          }
        </ul>
      }
      @if (hasPartial()) {
        <p class="chart-note" data-testid="chart-partial">{{ words.partialLine }}</p>
      }
    </app-chart-frame>
  `,
})
export class AreaChart extends TimeSeriesChart {
  protected readonly paths = computed(() => {
    const points = this.points();
    const { max } = this.scale();

    return this.series().map((series) => {
      const xy = points.map((point, index) => ({
        x: this.mirror(this.center(index)),
        y: this.y(point.values[series.key] ?? 0, max),
      }));
      const line = xy.map((p, index) => `${index === 0 ? 'M' : 'L'}${p.x},${p.y}`).join('');
      const first = xy[0];
      const last = xy.at(-1);
      const area =
        first && last ? `${line}L${last.x},${this.baseline()}L${first.x},${this.baseline()}Z` : '';

      return {
        key: series.key,
        tone: series.tone,
        line,
        area,
        endX: last?.x ?? 0,
        endY: last?.y ?? this.baseline(),
      };
    });
  });

  protected readonly crosshair = computed(() => {
    const index = this.active();
    const point = index === null ? undefined : this.points()[index];

    if (index === null || point === undefined) {
      return null;
    }

    return {
      x: this.mirror(this.center(index)),
      dots: this.series().map((series) => ({
        key: series.key,
        tone: series.tone,
        y: this.y(point.values[series.key] ?? 0),
      })),
    };
  });

  protected peak(point: ChartPoint): number {
    return Math.max(0, ...this.series().map((series) => point.values[series.key] ?? 0));
  }
}
