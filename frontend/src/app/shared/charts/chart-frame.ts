import { ChangeDetectionStrategy, Component, input, signal } from '@angular/core';
import { Icon } from '@app/shared/icon/icon';
import { CHART_WORDS, chartId } from './chart-model';

/**
 * The frame every plotted chart sits in: a title, a summary sentence and a
 * "View as table" switch.
 *
 * The summary is visible text, not an `aria-label`: it is the chart's text
 * alternative for everybody, and it says what the picture shows in one
 * sentence. The table is the chart's twin, holding every number the marks
 * draw, so a value is never reachable only by hovering.
 *
 * The switch keeps its place when it is pressed, so focus stays on it and a
 * keyboard reader can flip back and forth without hunting. The choice is the
 * reader's for this visit only; nothing is stored.
 */
@Component({
  selector: 'app-chart-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  host: { class: 'chart-frame' },
  template: `
    <div class="chart-frame-head">
      <div class="chart-frame-titles">
        @switch (level()) {
          @case (2) {
            <h2 class="chart-frame-title" [id]="headingId">{{ heading() }}</h2>
          }
          @case (4) {
            <h4 class="chart-frame-title" [id]="headingId">{{ heading() }}</h4>
          }
          @default {
            <h3 class="chart-frame-title" [id]="headingId">{{ heading() }}</h3>
          }
        }
        @if (summary()) {
          <p class="chart-frame-summary" data-testid="chart-summary">{{ summary() }}</p>
        }
      </div>
      <button
        type="button"
        class="btn btn-quiet btn-sm chart-frame-toggle"
        (click)="showTable.set(!showTable())"
        data-testid="chart-view-toggle"
      >
        <app-icon [name]="showTable() ? 'analytics' : 'table'" [strokeWidth]="1.75" />
        <span>{{ showTable() ? words.viewChart : words.viewTable }}</span>
      </button>
    </div>

    @if (showTable()) {
      <ng-content select="[chartTable]" />
    } @else {
      <ng-content select="[chartFigure]" />
    }
    <ng-content />
  `,
})
export class ChartFrame {
  readonly heading = input.required<string>();

  /** The heading level, so a chart fits the outline of the page it is on. */
  readonly level = input<2 | 3 | 4>(3);

  /** One plain sentence: what the chart shows, with the numbers that matter. */
  readonly summary = input('');

  protected readonly headingId = chartId('chart-heading');
  protected readonly showTable = signal(false);
  protected readonly words = CHART_WORDS;
}
