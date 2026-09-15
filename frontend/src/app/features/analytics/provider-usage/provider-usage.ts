import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { AnalyticsAiUsage, AnalyticsModelUsage, AnalyticsRatedCost } from '../analytics-api';

/**
 * What this workspace's calls to AI providers are estimated to have cost.
 *
 * <strong>Internal accounting, not billing.</strong> Every figure here is what
 * this platform reckons it paid a provider, computed from rates an operator
 * configured. There is no invoice, no plan, no credit, no quota and no amount
 * due, and no word on this card implies any of them. A customer charge would be
 * a different feature with different arithmetic and a different audience.
 *
 * <strong>Provider-reported, never estimated.</strong> The token counts are what
 * the provider itself said it processed. Nothing is derived from the length of a
 * prompt or an answer, so a call the provider reported nothing for contributes
 * nothing — not a zero that would flatter an average.
 *
 * <strong>Unknown is shown as unknown.</strong> A call this deployment has no
 * rate for is reported as unrated and counted separately, because unrated is not
 * free. Showing it as "$0.00" would be a lie with a currency symbol on it, and a
 * dash would read as one.
 *
 * <strong>Currencies are never added.</strong> This platform holds no exchange
 * rate, so two currencies are two figures with an explanation, and a model billed
 * in both carries no figure at all.
 */
@Component({
  selector: 'app-provider-usage',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './provider-usage.html',
  styleUrl: './provider-usage.scss',
})
export class ProviderUsageCard {
  /** Null when this workspace has never called a provider. */
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
      return 'Not priced';
    }

    try {
      return new Intl.NumberFormat(undefined, {
        style: 'currency',
        currency,
        minimumFractionDigits: 2,
        maximumFractionDigits: 6,
      }).format(amount);
    } catch {
      // An unrecognised currency code must not blank the card. The number and
      // the code are still the truth.
      return `${amount} ${currency}`;
    }
  }

  /** A count, grouped for reading. */
  protected tokens(count: number): string {
    return new Intl.NumberFormat().format(count);
  }

  /** The words a reader sees for each kind of provider operation. */
  protected operationName(operation: string): string {
    switch (operation) {
      case 'TextGeneration':
        return 'Generation';
      case 'Embedding':
        return 'Embedding';
      default:
        return operation;
    }
  }
}
