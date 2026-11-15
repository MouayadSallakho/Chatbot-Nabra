/**
 * What every Nabra chart is made of (Phase 36.7).
 *
 * The charts are small SVG components with no library behind them. They draw
 * only what a caller hands them -- counts the API returned -- and add no number
 * of their own beyond a sum or a share of the ones they were given. Colour comes
 * from the chart tokens and is always repeated in words (MASTER.md, section 3):
 * emerald is the AI, gold a person, customers and single series are neutral,
 * `current` is the period being read (emerald, calm-glass.md §2; Phase 39.7a),
 * and status colours appear only for a status, with its word.
 */

/**
 * What a series' colour means. Never a free colour: a role.
 *
 * There is no "success" tone on purpose. Green beside red fails the
 * colour-vision check (deutan ΔE 3.2 for the status pair), so outcome charts
 * emphasise instead: the expected outcome in neutral ink, the failure in
 * danger, anything unsettled in a lighter step of the neutral (`muted`), each
 * with its word and count.
 */
export type ChartTone =
  'ai' | 'human' | 'customer' | 'neutral' | 'current' | 'danger' | 'warning' | 'muted';

/** One thing being counted, in the order it is stacked or listed. */
export interface ChartSeries {
  readonly key: string;
  readonly label: string;
  readonly tone: ChartTone;
}

/** One position on a time axis: a day, with a value per series. */
export interface ChartPoint {
  readonly key: string;

  /** Short, for the axis ("18 Oct"). */
  readonly label: string;

  /** Long, for the readout and the table ("18 October 2026"). */
  readonly longLabel: string;
  readonly values: Readonly<Record<string, number>>;

  /** True for a period still running (today, so far). Drawn lighter and said in words. */
  readonly partial?: boolean;
}

/** One category on a bar chart: a channel, an agent, a kind of token. */
export interface ChartBar {
  readonly key: string;
  readonly label: string;
  readonly value: number;

  /** Optional words after the value ("100% of conversations"). */
  readonly note?: string;
}

/** One part of a whole on a stacked bar. */
export interface ChartPart {
  readonly key: string;
  readonly label: string;
  readonly value: number;
  readonly tone: ChartTone;
}

/** The words every chart shares. */
export const CHART_WORDS = {
  viewTable: $localize`:@@chart.viewTable:View as table`,
  viewChart: $localize`:@@chart.viewChart:View as chart`,
  total: $localize`:@@chart.total:Total`,
  soFar: $localize`:@@chart.soFar:so far`,
  partialColumns: $localize`:@@chart.partialColumns:The lighter column is today, still running: its figures are not final.`,
  partialLine: $localize`:@@chart.partialLine:The last point is today, still running: its figure is not final.`,
  keyboardHint: $localize`:@@chart.keyboardHint:Use the arrow keys to read each value; Home and End go to the first and the last.`,
} as const;

/**
 * A round number at or above `max`, and the ticks up to it.
 *
 * Steps are 1, 2 or 5 times a power of ten, never below 1: these are counts, and
 * an axis reading 0.5 conversations would invent a fraction. An empty or zero
 * series still gets an axis (0 to 1), so a chart of nothing is drawn as nothing
 * rather than as a full-height bar.
 */
export function niceScale(
  max: number,
  tickCount = 4,
): { readonly max: number; readonly ticks: readonly number[] } {
  const safe = Number.isFinite(max) && max > 0 ? max : 0;

  if (safe === 0) {
    return { max: 1, ticks: [0, 1] };
  }

  const rough = safe / tickCount;
  const power = 10 ** Math.floor(Math.log10(rough));
  const step = Math.max(
    1,
    [1, 2, 5, 10].map((m) => m * power).find((s) => s >= rough) ?? 10 * power,
  );
  const top = Math.ceil(safe / step) * step;
  const ticks: number[] = [];

  for (let tick = 0; tick <= top + step / 2; tick += step) {
    ticks.push(Math.round(tick));
  }

  return { max: top, ticks };
}

/** The sum of one point's values for the given series. */
export function pointTotal(point: ChartPoint, series: readonly ChartSeries[]): number {
  return series.reduce((sum, s) => sum + (point.values[s.key] ?? 0), 0);
}

/**
 * A rectangle whose data end is rounded and whose baseline is square
 * (dataviz mark spec: 4px data-end, square at the baseline).
 *
 * `end` says which side is the data end: the top of a column, or the inline
 * end of a bar in the reading direction -- which is the left in Arabic.
 */
export function roundedBar(
  x: number,
  y: number,
  width: number,
  height: number,
  end: 'top' | 'right' | 'left',
  radius = 4,
): string {
  if (width <= 0 || height <= 0) {
    return '';
  }

  const r = Math.max(
    0,
    Math.min(radius, end === 'top' ? width / 2 : height / 2, end === 'top' ? height : width),
  );
  const x2 = x + width;
  const y2 = y + height;

  switch (end) {
    case 'top':
      return `M${x},${y2}V${y + r}Q${x},${y} ${x + r},${y}H${x2 - r}Q${x2},${y} ${x2},${y + r}V${y2}Z`;
    case 'right':
      return `M${x},${y}H${x2 - r}Q${x2},${y} ${x2},${y + r}V${y2 - r}Q${x2},${y2} ${x2 - r},${y2}H${x}Z`;
    case 'left':
      return `M${x2},${y}H${x + r}Q${x},${y} ${x},${y + r}V${y2 - r}Q${x},${y2} ${x + r},${y2}H${x2}Z`;
  }
}

/**
 * Which axis labels to print so they never collide: every `step`-th one,
 * counted back from the newest so the latest day always has its label.
 */
export function labelStep(count: number, plotWidth: number, minLabelWidth = 56): number {
  if (count <= 1) {
    return 1;
  }

  const fit = Math.max(1, Math.floor(plotWidth / minLabelWidth));

  return Math.max(1, Math.ceil(count / fit));
}

let nextId = 0;

/** A document-unique id for a chart's heading, summary and readout. */
export function chartId(prefix: string): string {
  nextId += 1;

  return `${prefix}-${nextId}`;
}
