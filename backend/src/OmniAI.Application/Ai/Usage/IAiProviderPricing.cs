using OmniAI.Domain.Ai;

namespace OmniAI.Application.Ai.Usage;

/// <summary>
/// What this deployment has been told a provider charges.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A lookup, never a discovery.</strong> Nothing contacts a provider to
/// ask its prices, scrapes a page or infers a rate from a similar model. The
/// catalogue contains what an operator configured, and a model that is not in it
/// is unrated — which is a state the whole system is built to report honestly.
/// </para>
/// <para>
/// <strong>Versioned by date.</strong> A price change is a new entry with a later
/// effective date, and a call is rated with whatever was in force when it was
/// dispatched. That is what keeps last quarter's figures from moving when
/// somebody updates a price today.
/// </para>
/// <para>
/// Pricing is configuration, not a secret: there is nothing confidential in a
/// published rate card.
/// </para>
/// </remarks>
public interface IAiProviderPricing
{
    /// <summary>
    /// The rates in force for this model and operation at this instant, or
    /// <see langword="null"/> when none is configured.
    /// </summary>
    /// <remarks>
    /// Null is an ordinary answer, not an error. It produces an unrated
    /// operation: the call is still counted, its tokens are still exact, and no
    /// cost is claimed for it.
    /// </remarks>
    AiModelRates? Find(
        AiProvider provider,
        ModelName model,
        AiOperationType operationType,
        DateTimeOffset at);
}
