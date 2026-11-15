import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import { AreaChart } from './area-chart';
import { BarChart } from './bar-chart';
import {
  ChartBar,
  ChartPart,
  ChartPoint,
  ChartSeries,
  labelStep,
  niceScale,
  roundedBar,
} from './chart-model';
import { ColumnChart } from './column-chart';
import { Meter } from './meter';
import { Sparkline } from './sparkline';
import { StackedBar } from './stacked-bar';

/**
 * The chart components (Phase 36.7).
 *
 * What is proved: they draw exactly what they are given and nothing more (a
 * zero draws no mark, a running day is marked), they say it in words and in a
 * table, they mirror in Arabic, and the keyboard reads the same numbers the
 * pointer does. jsdom has no layout, so widths stay at each chart's default.
 */

const SERIES: readonly ChartSeries[] = [
  { key: 'customer', label: 'Customers', tone: 'customer' },
  { key: 'human', label: 'Team', tone: 'human' },
  { key: 'ai', label: 'AI', tone: 'ai' },
];

const POINTS: readonly ChartPoint[] = [
  {
    key: 'd1',
    label: 'Oct 16',
    longLabel: 'October 16, 2026',
    values: { customer: 4, human: 1, ai: 3 },
  },
  {
    key: 'd2',
    label: 'Oct 17',
    longLabel: 'October 17, 2026',
    values: { customer: 0, human: 0, ai: 0 },
  },
  {
    key: 'd3',
    label: 'Oct 18',
    longLabel: 'October 18, 2026',
    partial: true,
    values: { customer: 10, human: 6, ai: 0 },
  },
];

describe('chart model', () => {
  it('rounds the axis up to 1, 2 or 5 times a power of ten, and never draws a fraction of a count', () => {
    expect(niceScale(0)).toEqual({ max: 1, ticks: [0, 1] });
    expect(niceScale(352)).toEqual({ max: 400, ticks: [0, 100, 200, 300, 400] });
    expect(niceScale(45).max).toBe(60);
    expect(niceScale(3).ticks.every(Number.isInteger)).toBe(true);
  });

  it('rounds only the data end of a bar, and draws nothing for nothing', () => {
    expect(roundedBar(0, 0, 10, 0, 'top')).toBe('');
    expect(roundedBar(0, 10, 10, 50, 'top')).toContain('Q');
    expect(roundedBar(0, 0, 40, 12, 'left').startsWith('M40,0')).toBe(true);
  });

  it('thins axis labels so they cannot collide', () => {
    expect(labelStep(7, 600)).toBe(1);
    expect(labelStep(90, 600)).toBeGreaterThan(1);
  });
});

@Component({
  imports: [ColumnChart, AreaChart],
  template: `
    <app-column-chart
      heading="Messages per day"
      summary="Messages in this period."
      categoryLabel="Day"
      [series]="series"
      [points]="points()"
      data-testid="columns"
    />
    <app-area-chart
      heading="Conversations per day"
      categoryLabel="Day"
      [series]="line"
      [points]="points()"
      data-testid="area"
    />
  `,
})
class TimeHost {
  readonly series = SERIES;
  readonly line: readonly ChartSeries[] = [
    { key: 'customer', label: 'Customers', tone: 'neutral' },
  ];
  readonly points = signal<readonly ChartPoint[]>(POINTS);
}

describe('time charts', () => {
  let fixture: ComponentFixture<TimeHost>;

  function mount(language: AppLanguage = 'en'): void {
    TestBed.configureTestingModule({
      imports: [TimeHost],
      providers: [{ provide: APP_LANGUAGE, useValue: language }],
    });
    fixture = TestBed.createComponent(TimeHost);
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const columns = (): HTMLElement => host().querySelector('[data-testid="columns"]')!;

  it('draws one column per day, no mark for an empty day, and marks the running day', () => {
    mount();

    const groups = columns().querySelectorAll('.chart-column');

    expect(groups.length).toBe(3);
    expect(groups[0].querySelectorAll('path').length).toBe(3);
    expect(groups[1].querySelectorAll('path').length).toBe(0);
    expect(groups[2].classList.contains('is-partial')).toBe(true);
    expect(columns().querySelector('[data-testid="chart-partial"]')?.textContent).toContain(
      'still running',
    );
  });

  it('names every series in a legend with its total for the period', () => {
    mount();

    const legend = columns().querySelector('[data-testid="chart-legend"]')?.textContent ?? '';

    expect(legend).toContain('Customers');
    expect(legend).toContain('14');
    expect(legend).toContain('Team');
    expect(legend).toContain('7');
  });

  it('swaps the drawing for its table and back, keeping the switch where it was', () => {
    mount();

    const toggle = columns().querySelector<HTMLButtonElement>('[data-testid="chart-view-toggle"]')!;

    expect(toggle.textContent).toContain('View as table');
    toggle.click();
    fixture.detectChanges();

    const table = columns().querySelector('[data-testid="chart-table"]');

    expect(columns().querySelector('.chart-svg')).toBeNull();
    expect(table?.querySelectorAll('tbody tr').length).toBe(3);
    expect(table?.querySelector('thead')?.textContent).toContain('Total');
    expect(table?.textContent).toContain('(so far)');
    expect(columns().querySelector('[data-testid="chart-view-toggle"]')).toBe(toggle);
    expect(toggle.textContent).toContain('View as chart');
  });

  it('reads a day aloud from the keyboard, forward along the reading direction', () => {
    mount();

    const surface = columns().querySelector<HTMLElement>('[data-testid="chart-surface"]')!;

    surface.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();
    surface.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    fixture.detectChanges();

    const live = columns().querySelector('[aria-live="polite"]')?.textContent ?? '';

    expect(live).toContain('October 17, 2026');
    expect(live).toContain('Customers 0');

    surface.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    fixture.detectChanges();

    expect(columns().querySelector('[aria-live="polite"]')?.textContent).toContain('(so far)');
  });

  it('runs time from the right in Arabic, where the arrow keys swap', () => {
    mount('ar');

    const firstDay = columns().querySelector('.chart-column path')!.getAttribute('d')!;
    const x = Number(/^M([\d.]+)/.exec(firstDay)![1]);

    // The default width is 640; the first day sits on the right-hand side.
    expect(x).toBeGreaterThan(320);

    const surface = columns().querySelector<HTMLElement>('[data-testid="chart-surface"]')!;

    surface.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();

    expect(columns().querySelector('[aria-live="polite"]')?.textContent).toContain('October 16');
  });

  it('draws the area as a line with a marker on the newest day', () => {
    mount();

    const area = host().querySelector('[data-testid="area"]')!;

    expect(area.querySelector('.chart-line')?.getAttribute('d')).toMatch(/^M.*L.*L/);
    expect(area.querySelectorAll('.chart-dot').length).toBe(1);
    // One series needs no legend: the title says what is plotted.
    expect(area.querySelector('.chart-legend')).toBeNull();
  });

  it('takes its colour from the series role, the period being read included', () => {
    mount();

    const line = host().querySelector('[data-testid="area"] .chart-line')!;

    expect([...line.classList].some((name) => name.startsWith('chart-tone-'))).toBe(true);
  });
});

@Component({
  imports: [BarChart, StackedBar, Meter, Sparkline],
  template: `
    <app-bar-chart
      heading="Channels"
      [bars]="bars"
      categoryLabel="Channel"
      valueLabel="Conversations"
    />
    <app-stacked-bar
      heading="Who replied"
      [parts]="parts()"
      categoryLabel="Voice"
      valueLabel="Replies"
    />
    <app-meter [value]="12" [max]="10" state="critical" label="Members" valueText="12 of 10 used" />
    <app-sparkline [values]="spark()" />
  `,
})
class PartsHost {
  readonly bars: readonly ChartBar[] = [
    { key: 'web', label: 'Website chat', value: 9, note: '75%' },
    { key: 'wa', label: '<b>WhatsApp</b>', value: 3, note: '25%' },
  ];
  readonly parts = signal<readonly ChartPart[]>([
    { key: 'ai', label: 'AI', value: 3, tone: 'ai' },
    { key: 'human', label: 'Team', value: 1, tone: 'human' },
  ]);
  readonly spark = signal<readonly number[]>([1, 4, 2]);
}

describe('bars, parts, meter and sparkline', () => {
  let fixture: ComponentFixture<PartsHost>;

  function mount(language: AppLanguage = 'en'): void {
    TestBed.configureTestingModule({
      imports: [PartsHost],
      providers: [{ provide: APP_LANGUAGE, useValue: language }],
    });
    fixture = TestBed.createComponent(PartsHost);
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;

  it('puts every value at the tip of its bar in text, and labels as text', () => {
    mount();

    const rows = [...host().querySelectorAll('.bar-chart-row')].map((row) => row.textContent ?? '');

    expect(rows[0]).toContain('Website chat');
    expect(rows[0]).toContain('9');
    expect(rows[0]).toContain('75%');
    expect(rows[1]).toContain('<b>WhatsApp</b>');
    expect(host().querySelector('.bar-chart b')).toBeNull();
  });

  it('grows bars from the right in Arabic', () => {
    mount('ar');

    const d = host().querySelector('.bar-chart path')!.getAttribute('d')!;

    // The longest bar fills the track, so it starts at the track's right edge.
    expect(d.startsWith('M240,0')).toBe(true);
  });

  it('says each part with its count and share, and draws no sliver for nothing', () => {
    mount();

    const legend =
      host().querySelector('app-stacked-bar [data-testid="chart-legend"]')?.textContent ?? '';

    expect(legend).toContain('AI');
    expect(legend).toContain('75%');
    expect(legend).toContain('25%');

    fixture.componentInstance.parts.set([
      { key: 'ai', label: 'AI', value: 0, tone: 'ai' },
      { key: 'human', label: 'Team', value: 0, tone: 'human' },
    ]);
    fixture.detectChanges();

    expect(host().querySelectorAll('app-stacked-bar .chart-mark').length).toBe(0);
    expect(
      host().querySelector('app-stacked-bar [data-testid="chart-legend"]')?.textContent,
    ).toContain('0');
  });

  it('is a real meter, clamped to its maximum, with the value in words', () => {
    mount();

    const meter = host().querySelector('app-meter')!;

    expect(meter.getAttribute('role')).toBe('meter');
    expect(meter.getAttribute('aria-valuenow')).toBe('10');
    expect(meter.getAttribute('aria-valuemax')).toBe('10');
    expect(meter.getAttribute('aria-valuetext')).toBe('12 of 10 used');
    expect(meter.classList.contains('is-critical')).toBe(true);
  });

  it('keeps the sparkline away from assistive technology and draws nothing from one value', () => {
    mount();

    const spark = host().querySelector('app-sparkline')!;

    expect(spark.getAttribute('aria-hidden')).toBe('true');
    expect(spark.querySelector('.chart-line')).not.toBeNull();

    fixture.componentInstance.spark.set([5]);
    fixture.detectChanges();

    expect(spark.querySelector('.chart-line')).toBeNull();
  });
});
