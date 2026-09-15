import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AnalyticsAiUsage, AnalyticsModelUsage } from '../analytics-api';
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
  };

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

  it('renders nothing at all without a provider call to report', () => {
    mount(null);

    // Not an empty card, and not a card of noughts. There is nothing to say.
    expect((fixture.nativeElement as HTMLElement).querySelector('.card')).toBeNull();
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
});
