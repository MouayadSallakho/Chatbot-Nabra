import { Directive, ElementRef, computed, inject, input, signal } from '@angular/core';
import { measureWidth } from './chart-measure';
import { APP_LANGUAGE, directionOf } from '@app/core/i18n/language';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import {
  CHART_WORDS,
  ChartPoint,
  ChartSeries,
  chartId,
  labelStep,
  niceScale,
  pointTotal,
} from './chart-model';

/** Inline space for the value axis, beside the plot. */
const AXIS_GAP = 8;

/** Space above the plot, so the top tick label is not clipped. */
export const PLOT_TOP = 10;

/** The band under the plot that holds the day labels. */
export const X_AXIS_BAND = 26;

/**
 * What the column and area charts share: the time axis, the value axis, the
 * mirroring, and the readout.
 *
 * <strong>Measured, not stretched.</strong> The chart reads its own width and
 * draws in pixels, so text is never squashed by a `viewBox` and a column is
 * never wider than 24px, whatever the screen.
 *
 * <strong>Mirrored in Arabic.</strong> Time runs in the reading direction: the
 * first day sits at the inline start, so in Arabic the newest day is on the
 * left and the value axis on the right. The SVG itself is laid out
 * left-to-right and every x is mirrored here, which keeps text anchors simple.
 *
 * <strong>One tab stop.</strong> The plot is a single focusable group; the
 * arrow keys walk the days (the key that points along the time direction moves
 * forward, so it swaps in Arabic), and the day's numbers are spoken once in a
 * polite live region. A pointer gets the same readout as a tooltip. Neither is
 * the only way to a number: the table holds every value.
 */
@Directive()
export abstract class TimeSeriesChart {
  readonly heading = input.required<string>();
  readonly level = input<2 | 3 | 4>(3);
  readonly summary = input('');

  /** Stacking or drawing order: the first series sits on the baseline. */
  readonly series = input.required<readonly ChartSeries[]>();
  readonly points = input.required<readonly ChartPoint[]>();

  /** The plot's own height in pixels, without the axis band. */
  readonly plotHeight = input(180);

  /** The table's first column heading ("Day"). */
  readonly categoryLabel = input('');

  protected readonly format = inject(LocaleFormat);
  protected readonly rtl = directionOf(inject(APP_LANGUAGE)) === 'rtl';
  protected readonly words = CHART_WORDS;
  protected readonly hintId = chartId('chart-hint');

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The drawing width; a sensible default until the first measurement. */
  protected readonly width = signal(640);

  /** The day being read, by pointer or keyboard; null when none. */
  protected readonly active = signal<number | null>(null);

  /** What the live region says; set only by the keyboard, so hovering is silent. */
  protected readonly spoken = signal('');

  protected readonly scale = computed(() =>
    niceScale(Math.max(0, ...this.points().map((point) => this.peak(point)))),
  );

  /** Room for the widest tick label, in the chart's own figures. */
  protected readonly axisWidth = computed(() => {
    const longest = Math.max(...this.scale().ticks.map((tick) => this.format.number(tick).length));

    return AXIS_GAP + 8 + longest * 7.5;
  });

  protected readonly plotStart = computed(() => this.axisWidth());
  protected readonly plotEnd = computed(() => this.width() - 4);
  protected readonly plotWidth = computed(() => Math.max(1, this.plotEnd() - this.plotStart()));
  protected readonly band = computed(() => this.plotWidth() / Math.max(1, this.points().length));
  protected readonly svgHeight = computed(() => PLOT_TOP + this.plotHeight() + X_AXIS_BAND);
  protected readonly baseline = computed(() => PLOT_TOP + this.plotHeight());

  /** Gridlines and their labels, already mirrored. */
  protected readonly gridLines = computed(() => {
    const { max, ticks } = this.scale();

    return ticks.map((tick) => {
      const y = this.y(tick, max);

      return {
        tick,
        y,
        label: this.format.number(tick),
        x1: this.mirror(this.plotStart()),
        x2: this.mirror(this.plotEnd()),
        labelX: this.mirror(this.plotStart() - AXIS_GAP),
      };
    });
  });

  /** The tick labels' anchor: towards the plot, on whichever side it is. */
  protected readonly tickAnchor = this.rtl ? 'start' : 'end';

  /** Day labels that fit, counted back from the newest so it is always named. */
  protected readonly dayLabels = computed(() => {
    const points = this.points();
    const step = labelStep(points.length, this.plotWidth());

    return points
      .map((point, index) => ({ point, index }))
      .filter(({ index }) => (points.length - 1 - index) % step === 0)
      .map(({ point, index }) => ({
        key: point.key,
        label: point.label,
        x: this.mirror(this.center(index)),
      }));
  });

  /** The highlighted day's band, behind its marks. */
  protected readonly activeBand = computed(() => {
    const index = this.active();

    if (index === null) {
      return null;
    }

    const start = this.plotStart() + this.band() * index;

    return { x: this.rtl ? this.mirror(start + this.band()) : start, width: this.band() };
  });

  /** The readout for the highlighted day, positioned in logical pixels. */
  protected readonly readout = computed(() => {
    const index = this.active();
    const point = index === null ? undefined : this.points()[index];

    if (index === null || point === undefined) {
      return null;
    }

    const width = 196;
    const inset = Math.min(
      Math.max(0, this.center(index) - width / 2),
      Math.max(0, this.width() - width),
    );

    return {
      title: point.partial ? `${point.longLabel} (${this.words.soFar})` : point.longLabel,
      inset,
      rows: [...this.series()].reverse().map((series) => ({
        key: series.key,
        tone: series.tone,
        label: series.label,
        value: this.format.number(point.values[series.key] ?? 0),
      })),
    };
  });

  /** Whether a point is still running (today, so far), which the chart then says in words. */
  protected readonly hasPartial = computed(() =>
    this.points().some((point) => point.partial === true),
  );

  /** Totals per series over the whole period, for the legend. */
  protected readonly totals = computed(() =>
    this.series().map((series) => ({
      ...series,
      total: this.format.number(
        this.points().reduce((sum, point) => sum + (point.values[series.key] ?? 0), 0),
      ),
    })),
  );

  constructor() {
    measureWidth(() => this.host.nativeElement, this.width);
  }

  /** The value a point reaches on the value axis. */
  protected abstract peak(point: ChartPoint): number;

  /** A logical x (from the inline start) as a drawing x. */
  protected mirror(x: number): number {
    return this.rtl ? this.width() - x : x;
  }

  /** The logical centre of a day's band. */
  protected center(index: number): number {
    return this.plotStart() + this.band() * (index + 0.5);
  }

  protected y(value: number, max = this.scale().max): number {
    return PLOT_TOP + this.plotHeight() - (value / max) * this.plotHeight();
  }

  /** One point's value for one series, formatted; a missing value is zero. */
  protected count(point: ChartPoint, key: string): string {
    return this.format.number(point.values[key] ?? 0);
  }

  protected total(point: ChartPoint): number {
    return pointTotal(point, this.series());
  }

  protected onPointerMove(event: PointerEvent): void {
    const box = (event.currentTarget as Element).getBoundingClientRect();
    const drawn = event.clientX - box.left;
    const logical = this.rtl ? box.width - drawn : drawn;
    const index = Math.floor((logical - this.plotStart()) / this.band());

    this.active.set(index >= 0 && index < this.points().length ? index : null);
  }

  protected onPointerLeave(): void {
    this.active.set(null);
  }

  protected onBlur(): void {
    this.active.set(null);
    this.spoken.set('');
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.points().length;

    if (count === 0) {
      return;
    }

    const current = this.active();
    const forward = this.rtl ? 'ArrowLeft' : 'ArrowRight';
    const back = this.rtl ? 'ArrowRight' : 'ArrowLeft';
    let next: number | null;

    switch (event.key) {
      case forward:
        next = current === null ? 0 : Math.min(count - 1, current + 1);
        break;
      case back:
        next = current === null ? count - 1 : Math.max(0, current - 1);
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = count - 1;
        break;
      case 'Escape':
        this.active.set(null);
        this.spoken.set('');
        return;
      default:
        return;
    }

    event.preventDefault();
    this.active.set(next);
    this.spoken.set(this.sentence(next));
  }

  /** One day's numbers as words, for the live region. */
  private sentence(index: number): string {
    const point = this.points()[index];

    if (!point) {
      return '';
    }

    const parts = this.series().map(
      (series) => `${series.label} ${this.format.number(point.values[series.key] ?? 0)}`,
    );
    const list = new Intl.ListFormat(this.format.language, { type: 'unit', style: 'short' });
    const title = point.partial ? `${point.longLabel} (${this.words.soFar})` : point.longLabel;

    return `${title}: ${list.format(parts)}`;
  }
}
