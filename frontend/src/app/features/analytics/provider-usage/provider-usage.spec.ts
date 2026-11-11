import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AnalyticsAiUsage, AnalyticsModelUsage, AnalyticsPurposeUsage } from '../analytics-api';
import { ProviderUsageCard } from './provider-usage';

/**
 * The provider usage card, on its own.
 *
 * The dashboard's own spec proves this card in place, with real data through the
 * real page. What is proved here is the handful of edge cases that are awkward
 * to reach from there and expensive to get wrong: an amount too small to show at
 * two decimal places, a currency code nobody recognises, and the difference
 * between a cost of nought and no cost at all.
 */
describe('ProviderUsageCard', () => {
  const usage: AnalyticsAiUsage = {
    calls: 2,
    generationCalls: 2,
    embeddingCalls: 0,
    succeeded: 2,
    failed: 0,
    unresolved: 0,
    generationInputTokens: 2_468,
    generationCachedInputTokens: 0,
    generationOutputTokens: 0,
    generationReasoningTokens: 0,
    embeddingInputTokens: 0,
    unratedCalls: 0,
    ratedCost: [{ currency: 'USD', estimatedCost: 0.004936, calls: 2 }],
    byPurpose: [],
  };

  /** One purpose's row, zeroes unless given. */
  function purpose(
    code: string,
    values: Partial<AnalyticsPurposeUsage> = {},
  ): AnalyticsPurposeUsage {
    return {
      purpose: code,
      calls: 0,
      succeeded: 0,
      failed: 0,
      unresolved: 0,
      inputTokens: 0,
      outputTokens: 0,
      unratedCalls: 0,
      ratedCost: [],
      ...values,
    };
  }

  let fixture: ComponentFixture<ProviderUsageCard>;

  function mount(
    reported: AnalyticsAiUsage | null,
    models: readonly AnalyticsModelUsage[] = [],
  ): void {
    TestBed.configureTestingModule({ imports: [ProviderUsageCard] });

    fixture = TestBed.createComponent(ProviderUsageCard);

    fixture.componentRef.setInput('usage', reported);
    fixture.componentRef.setInput('models', models);

    fixture.detectChanges();
  }

  const text = (): string => (fixture.nativeElement as HTMLElement).textContent ?? '';

  it('says in one sentence that there was nothing to report, without a figure', () => {
    mount(null);

    const host = fixture.nativeElement as HTMLElement;

    // Not a card of noughts: no chart, no amount, no currency. One sentence.
    expect(host.querySelector('[data-testid="ai-usage"]')).toBeNull();
    expect(host.querySelector('.chart')).toBeNull();
    expect(host.querySelector('[data-testid="ai-usage-none"]')?.textContent).toContain(
      'No calls to an AI provider in this period',
    );
    expect(text()).not.toMatch(/\$|0\.00/);
  });

  it('draws calls and tokens by kind of operation, each with its table', () => {
    mount({ ...usage, embeddingCalls: 1, calls: 3, embeddingInputTokens: 900 });

    const host = fixture.nativeElement as HTMLElement;
    const charts = [...host.querySelectorAll('app-bar-chart')];

    expect(
      charts.map((chart) => chart.querySelector('.chart-frame-title')?.textContent?.trim()),
    ).toEqual(['Calls by kind of operation', 'Tokens by kind']);
    expect(charts[1].textContent).toContain('Embedding input');
    expect(charts[1].textContent).toContain('900');

    for (const chart of charts) {
      expect(chart.querySelector('[data-testid="chart-view-toggle"]')).not.toBeNull();
    }
  });

  it('shows an amount smaller than a penny rather than rounding it away', () => {
    mount(usage);

    // At two decimal places this reads "$0.00", which is false: two real calls
    // were made and a real, small amount was spent.
    expect(text()).toContain('0.004936');
  });

  it('shows a whole amount at the ordinary two places', () => {
    mount({ ...usage, ratedCost: [{ currency: 'USD', estimatedCost: 12.5, calls: 2 }] });

    // Two places is the floor, so an ordinary amount reads ordinarily.
    expect(text()).toContain('12.50');
    expect(text()).not.toContain('12.500000');
  });

  it('does not round a month of small charges up to the nearest penny', () => {
    mount({ ...usage, ratedCost: [{ currency: 'USD', estimatedCost: 0.038436, calls: 63 }] });

    // "$0.04" would be a different number from the one that was computed, and
    // this is a figure somebody may check against a provider's own statement.
    expect(text()).toContain('0.038436');
  });

  it('survives a currency code the browser does not know', () => {
    mount({ ...usage, ratedCost: [{ currency: 'XYZ', estimatedCost: 1.5, calls: 2 }] });

    // The formatter refuses the code; the number and the code are still true,
    // and a blank card would be worse than an unstyled one.
    expect(text()).toContain('1.5');
    expect(text()).toContain('XYZ');
  });

  it('does not point at a figure that is not there when nothing could be priced', () => {
    mount({ ...usage, unratedCalls: 2, ratedCost: [] });

    const note = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="ai-usage-unrated"]',
    );

    // Still says unrated is not free, but stops claiming there is a total to be
    // outside of — a reader would go looking for a number that is not there.
    expect(note?.textContent).toContain('Unrated does not mean free');
    expect(note?.textContent).not.toContain('above');
  });

  it('says a model billed in two currencies has no single cost', () => {
    mount(usage, [
      {
        provider: 'OpenAi',
        model: 'test-model-x',
        operationType: 'TextGeneration',
        calls: 2,
        inputTokens: 2_468,
        outputTokens: 0,
        estimatedCost: null,
        currency: null,
        unratedCalls: 0,
      },
    ]);

    // "Not priced" rather than a dash, which reads as zero, or a converted
    // figure, which would need an exchange rate this platform does not have.
    expect(text()).toContain('Not priced');
  });

  describe('calls by purpose (E5)', () => {
    const byPurpose: readonly AnalyticsPurposeUsage[] = [
      // Deliberately out of order: the card keeps the fixed order whatever arrives.
      purpose('AgentRetrievalTest'),
      purpose('ConversationGeneration', {
        calls: 30,
        succeeded: 27,
        failed: 2,
        unresolved: 1,
        inputTokens: 12_000,
        outputTokens: 3_400,
        unratedCalls: 3,
        ratedCost: [
          { currency: 'USD', estimatedCost: 0.25, calls: 20 },
          { currency: 'EUR', estimatedCost: 0.1, calls: 7 },
        ],
      }),
      purpose('KnowledgeIndexEmbedding', { calls: 4, succeeded: 4, inputTokens: 9_000 }),
      purpose('ConversationRetrievalEmbedding'),
      purpose('AgentPreviewGeneration', { calls: 1, succeeded: 1, unratedCalls: 1 }),
    ];

    const rows = (): HTMLTableRowElement[] => [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLTableRowElement>(
        '[data-testid="ai-purposes"] tbody tr',
      ),
    ];

    const cells = (row: HTMLTableRowElement): string[] =>
      [...row.querySelectorAll('th, td')].map((cell) =>
        (cell.textContent ?? '').replace(/\s+/g, ' ').trim(),
      );

    it('lists every purpose in the fixed order, zeroes included', () => {
      mount({ ...usage, byPurpose });

      expect(rows().map((row) => cells(row)[0])).toEqual([
        'Replies to customers',
        'Knowledge searches for replies',
        'Indexing knowledge documents',
        'Agent previews',
        'Knowledge search tests',
      ]);
      expect(cells(rows()[4])).toEqual(['Knowledge search tests', '0', '0', '0', '—']);
    });

    it('shows each currency on its own line and never adds them', () => {
      mount({ ...usage, byPurpose });

      const cost = rows()[0].querySelectorAll('td')[3];
      const lines = [...cost.querySelectorAll('.purpose-cost')].map((line) => line.textContent);

      expect(lines).toEqual(['$0.25', '€0.10']);
      expect(cost.textContent).not.toContain('0.35');
    });

    it('says unrated calls in words, and failures and unknown outcomes beside the calls', () => {
      mount({ ...usage, byPurpose });

      expect(rows()[0].querySelector('[data-testid="purpose-unpriced"]')?.textContent).toBe(
        '3 calls not priced',
      );
      expect(rows()[3].querySelector('[data-testid="purpose-unpriced"]')?.textContent).toBe(
        '1 call not priced',
      );
      // A row with only unpriced calls shows no dash beside its sentence.
      expect(cells(rows()[3])[4]).toBe('1 call not priced');
      const calls = rows()[0].querySelectorAll('td')[0];

      expect(
        [...calls.querySelectorAll('.purpose-meta')].map((line) =>
          line.textContent?.replace(/\s+/g, ' ').trim(),
        ),
      ).toEqual(['Failed: 2', 'Outcome unknown: 1']);
    });

    it('keeps a purpose a later server adds, named by its code, left to right', () => {
      mount({ ...usage, byPurpose: [...byPurpose, purpose('FutureThing', { calls: 2 })] });

      const last = rows().at(-1)!;

      expect(last.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
      expect(cells(last)[0]).toBe('FutureThing');
    });

    it('has no purpose table without calls', () => {
      mount(null);

      expect(
        (fixture.nativeElement as HTMLElement).querySelector('[data-testid="ai-purposes"]'),
      ).toBeNull();
    });
  });
});
