import { ChangeDetectionStrategy, Component, computed, input, inject } from '@angular/core';
import { AnalyticsAiUsage, AnalyticsModelUsage, AnalyticsRatedCost } from '../analytics-api';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import { BarChart } from '@app/shared/charts/bar-chart';
import { ChartBar, ChartPart } from '@app/shared/charts/chart-model';
import { StackedBar } from '@app/shared/charts/stacked-bar';
import { ScrollRegion } from '@app/shared/scroll-region/scroll-region';

/**
 * What this workspace's calls to AI providers are estimated to have cost.
 *
 * <strong>Internal accounting, not billing.</strong> Every figure here is what
 * this platform reckons it paid a provider, computed from rates an operator
 * configured. There is no invoice, no plan, no credit, no quota and no amount
 * due, and no word in this section implies any of them.
 *
 * <strong>By kind of operation, because that is what the API reports.</strong>
 * Each call is recorded with its purpose (answering a customer, searching
 * knowledge, indexing a document, previewing an agent), but the dashboard
 * returns calls and tokens per operation -- generation or embedding -- and per
 * model. That is what is drawn; a purpose breakdown is a backend ask, not a
 * guess made here.
 *
 * <strong>Provider-reported, never estimated.</strong> The token counts are what
 * the provider itself said it processed. A call the provider reported nothing
 * for contributes nothing, not a zero that would flatter an average.
 *
 * <strong>Unknown is shown as unknown.</strong> A call with no configured rate is
 * counted as unrated and said to be so: unrated is not free, and "$0.00" would
 * be a lie with a currency symbol on it. A model billed in two currencies reads
 * "Not priced". Currencies are never added together.
 */
@Component({
  selector: 'app-provider-usage',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ScrollRegion, BarChart, StackedBar],
  templateUrl: './provider-usage.html',
  styleUrl: './provider-usage.scss',
})
export class ProviderUsageCard {
  private readonly locale = inject(LocaleFormat);

  /** Null when this workspace made no provider call in the period. */
  readonly usage = input<AnalyticsAiUsage | null>(null);

  /** The busiest models, or empty when there were no calls. */
  readonly models = input<readonly AnalyticsModelUsage[]>([]);

  /**
   * Whether anything at all could be priced.
   *
   * Distinguishes "nothing could be priced" from "the price was nought", which
   * look alike on a screen and mean very different things.
   */
  protected readonly hasRatedCost = computed(() => (this.usage()?.ratedCost.length ?? 0) > 0);

  protected readonly callBars = computed<readonly ChartBar[]>(() => {
    const usage = this.usage();

    if (!usage) {
      return [];
    }

    return [
      {
        key: 'generation',
        label: this.operationName('TextGeneration'),
        value: usage.generationCalls,
      },
      { key: 'embedding', label: this.operationName('Embedding'), value: usage.embeddingCalls },
    ];
  });

  protected readonly callsSummary = computed(() => {
    const usage = this.usage();
    const calls = this.locale.number(usage?.calls ?? 0);
    const generation = this.locale.number(usage?.generationCalls ?? 0);
    const embedding = this.locale.number(usage?.embeddingCalls ?? 0);

    return $localize`:@@usage.calls.summary:Provider calls in this period: ${calls}:calls:. Generation: ${generation}:generation:, embedding: ${embedding}:embedding:.`;
  });

  protected readonly outcomeParts = computed<readonly ChartPart[]>(() => {
    const usage = this.usage();
    const parts: ChartPart[] = [
      {
        key: 'succeeded',
        label: $localize`:@@usage.outcome.answered:Answered`,
        value: usage?.succeeded ?? 0,
        tone: 'neutral',
      },
      {
        key: 'failed',
        label: $localize`:@@usage.outcome.failed:Failed`,
        value: usage?.failed ?? 0,
        tone: 'danger',
      },
    ];

    if ((usage?.unresolved ?? 0) > 0) {
      parts.push({
        key: 'unresolved',
        label: $localize`:@@usage.outcome.unresolved:Outcome unknown`,
        value: usage?.unresolved ?? 0,
        tone: 'muted',
      });
    }

    return parts;
  });

  protected readonly outcomeSummary = computed(() => {
    const usage = this.usage();

    if ((usage?.unresolved ?? 0) > 0) {
      const unresolved = this.locale.number(usage?.unresolved ?? 0);

      return $localize`:@@usage.outcome.summaryUnresolved:How the calls ended. ${unresolved}:unresolved: timed out or stopped before an answer, so the provider may still have counted them.`;
    }

    return $localize`:@@usage.outcome.summary:How the calls ended, as each call was recorded.`;
  });

  /** Tokens per kind, with what part of them was cached or spent on reasoning. */
  protected readonly tokenBars = computed<readonly ChartBar[]>(() => {
    const usage = this.usage();

    if (!usage) {
      return [];
    }

    const cached = this.locale.number(usage.generationCachedInputTokens);
    const reasoning = this.locale.number(usage.generationReasoningTokens);
    const bars: ChartBar[] = [
      {
        key: 'generation-input',
        label: $localize`:@@usage.tokens.generationInput:Generation input`,
        value: usage.generationInputTokens,
        note:
          usage.generationCachedInputTokens > 0
            ? $localize`:@@usage.tokens.cached:(cached: ${cached}:cached:)`
            : '',
      },
      {
        key: 'generation-output',
        label: $localize`:@@usage.tokens.generationOutput:Generation output`,
        value: usage.generationOutputTokens,
        note:
          usage.generationReasoningTokens > 0
            ? $localize`:@@usage.tokens.reasoning:(reasoning: ${reasoning}:reasoning:)`
            : '',
      },
    ];

    if (usage.embeddingCalls > 0) {
      bars.push({
        key: 'embedding-input',
        label: $localize`:@@usage.tokens.embeddingInput:Embedding input`,
        value: usage.embeddingInputTokens,
      });
    }

    return bars;
  });

  protected readonly tokensSummary = $localize`:@@usage.tokens.summary:Tokens the providers themselves reported. Cached input is part of input, and reasoning is part of output; neither is counted twice.`;

  /** "9 calls are not covered by any configured rate", with every Arabic plural form. */
  protected readonly unratedSentence = computed(() => {
    const usage = this.usage();
    const count = usage?.unratedCalls ?? 0;
    const n = this.locale.number(count);

    if (count === 0) {
      return '';
    }

    return this.hasRatedCost()
      ? pluralMessage(this.locale.language, count, {
          zero: $localize`:@@usage.unrated.outside.zero:No call is outside the figure above. Unrated does not mean free.`,
          one: $localize`:@@usage.unrated.outside.one:${n}:count: call is not covered by any configured rate and is outside the figure above. Unrated does not mean free.`,
          two: $localize`:@@usage.unrated.outside.two:${n}:count: calls are not covered by any configured rate and are outside the figure above. Unrated does not mean free.`,
          few: $localize`:@@usage.unrated.outside.few:${n}:count: calls are not covered by any configured rate and are outside the figure above. Unrated does not mean free.`,
          many: $localize`:@@usage.unrated.outside.many:${n}:count: calls are not covered by any configured rate and are outside the figure above. Unrated does not mean free.`,
          other: $localize`:@@usage.unrated.outside.other:${n}:count: calls are not covered by any configured rate and are outside the figure above. Unrated does not mean free.`,
        })
      : pluralMessage(this.locale.language, count, {
          zero: $localize`:@@usage.unrated.all.zero:No call could be priced. Unrated does not mean free.`,
          one: $localize`:@@usage.unrated.all.one:${n}:count: call could not be priced. Unrated does not mean free.`,
          two: $localize`:@@usage.unrated.all.two:${n}:count: calls could not be priced. Unrated does not mean free.`,
          few: $localize`:@@usage.unrated.all.few:${n}:count: calls could not be priced. Unrated does not mean free.`,
          many: $localize`:@@usage.unrated.all.many:${n}:count: calls could not be priced. Unrated does not mean free.`,
          other: $localize`:@@usage.unrated.all.other:${n}:count: calls could not be priced. Unrated does not mean free.`,
        });
  });

  /**
   * An estimated cost, in its own currency.
   *
   * Formatted in the reader's locale, to two places at least and six at most.
   *
   * Two places is not enough on its own: these are amounts per million tokens,
   * so a real day of traffic can come to a fraction of a penny and a real month
   * to a few pence. Rounding to two would show genuine spend as nothing, and
   * round a figure somebody may be checking against a provider's own statement.
   * Six is where the stored `numeric(18,6)` ends, so nothing is invented either.
   */
  protected cost(rated: AnalyticsRatedCost | AnalyticsModelUsage): string {
    const amount = rated.estimatedCost;
    const currency = rated.currency;

    if (amount === null || currency === null) {
      return $localize`:@@usage.notPriced:Not priced`;
    }

    try {
      return this.locale.currency(amount, currency, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 6,
      });
    } catch {
      // An unrecognised currency code must not blank the section. The number and
      // the code are still the truth.
      return `${amount} ${currency}`;
    }
  }

  /** "estimated across 38 calls". */
  protected acrossCalls(calls: number): string {
    const n = this.locale.number(calls);

    return pluralMessage(this.locale.language, calls, {
      zero: $localize`:@@usage.across.zero:estimated across no calls`,
      one: $localize`:@@usage.across.one:estimated across ${n}:count: call`,
      two: $localize`:@@usage.across.two:estimated across ${n}:count: calls`,
      few: $localize`:@@usage.across.few:estimated across ${n}:count: calls`,
      many: $localize`:@@usage.across.many:estimated across ${n}:count: calls`,
      other: $localize`:@@usage.across.other:estimated across ${n}:count: calls`,
    });
  }

  /** A count, grouped for reading. */
  protected tokens(count: number): string {
    return this.locale.number(count);
  }

  protected unratedNote(count: number): string {
    const n = this.locale.number(count);

    return $localize`:@@usage.model.unrated:unrated: ${n}:count:`;
  }

  /** The words a reader sees for each kind of provider operation. */
  protected operationName(operation: string): string {
    switch (operation) {
      case 'TextGeneration':
        return $localize`:@@usage.operation.generation:Generation`;
      case 'Embedding':
        return $localize`:@@usage.operation.embedding:Embedding`;
      default:
        return operation;
    }
  }
}
