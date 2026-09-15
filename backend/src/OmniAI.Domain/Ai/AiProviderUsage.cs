using System.Globalization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Ai;

/// <summary>Identity of one accounted provider operation.</summary>
public readonly record struct AiProviderUsageId
{
    private AiProviderUsageId(Guid value) => Value = value;

    public Guid Value { get; }

    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>
    /// A new identifier, generated here.
    /// </summary>
    /// <remarks>
    /// By this platform, never by the provider. The row exists before the
    /// provider is called, so there is nothing else it could come from — and an
    /// id taken from a response would be unavailable in exactly the ambiguous
    /// case that most needs one.
    /// </remarks>
    public static AiProviderUsageId New() => new(Guid.CreateVersion7());

    public static AiProviderUsageId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("A usage id must not be an empty GUID.")
            : new AiProviderUsageId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// One real call to an external AI provider, and what it cost.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One row is one provider request.</strong> Not one chunk, not one
/// vector, not one logical attempt — one HTTP operation that a provider could
/// bill for. An embedding batch of thirty-two inputs sent in a single request is
/// one row, because that is what appears on an invoice.
/// </para>
/// <para>
/// <strong>This is accounting, not billing.</strong> It records what the
/// platform spent with a provider. It is not a customer charge, has no markup,
/// no plan, no quota and no invoice, and the estimated cost on it is exactly
/// that — an internal estimate computed from configured rates.
/// </para>
/// <para>
/// <strong>It is not the source of truth for AI attempts.</strong> A conversation
/// turn records whether the platform tried to answer somebody; this records
/// whether money left the building. They differ on purpose: a turn refused before
/// dispatch costs nothing and appears here not at all.
/// </para>
/// <para>
/// <strong>It holds no content.</strong> No prompt, no completion, no
/// instructions, no grounding, no knowledge excerpt, no customer message, no
/// vector, no key, no request or response body. Counts, identifiers and rates —
/// nothing that could reconstruct what was said.
/// </para>
/// <para>
/// <strong>Settling is idempotent.</strong> A finalisation arriving twice — a
/// retried handler, two threads racing on the same response — settles once and
/// reports that it did nothing the second time. Cost is never doubled.
/// </para>
/// </remarks>
public sealed class AiProviderUsage
{
    private AiProviderUsage(
        AiProviderUsageId id,
        TenantId tenantId,
        AiProvider provider,
        AiOperationType operationType,
        AiUsagePurpose purpose,
        ModelName requestedModel,
        DateTimeOffset startedAt)
    {
        Id = id;
        TenantId = tenantId;
        Provider = provider;
        OperationType = operationType;
        Purpose = purpose;
        RequestedModel = requestedModel;
        Status = AiOperationStatus.Started;
        StartedAt = startedAt;
        CreatedAt = startedAt;
        Usage = ProviderTokenUsage.Unreported;
    }

    public AiProviderUsageId Id { get; }

    /// <summary>
    /// Whose spend this is.
    /// </summary>
    /// <remarks>
    /// Never inferred. It is supplied by the caller at the boundary, because a
    /// provider call attributed to the wrong workspace is worse than one
    /// attributed to none.
    /// </remarks>
    public TenantId TenantId { get; }

    public AiProvider Provider { get; }

    public AiOperationType OperationType { get; }

    public AiUsagePurpose Purpose { get; }

    /// <summary>The model this platform asked for.</summary>
    public ModelName RequestedModel { get; }

    /// <summary>
    /// The model the provider says it used, when it says.
    /// </summary>
    /// <remarks>
    /// Kept beside the requested name rather than replacing it: a provider
    /// answering a request for one model with a dated variant of another is
    /// something an operator needs to be able to see.
    /// </remarks>
    public ModelName? ResolvedModel { get; private set; }

    public AiOperationStatus Status { get; private set; }

    /// <summary>
    /// When the request was dispatched, in UTC.
    /// </summary>
    /// <remarks>
    /// <strong>This is the instant every report groups by.</strong> One timestamp,
    /// used consistently: an operation that starts before midnight and finishes
    /// after belongs to the day it was dispatched, so a call is counted once and
    /// in a period that had already begun when the money was committed.
    /// </remarks>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When it was settled. Null while still unresolved.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>What the provider reported. Absent counts stay absent.</summary>
    public ProviderTokenUsage Usage { get; private set; }

    /// <summary>
    /// The internal estimate, when every rate it needed was configured.
    /// </summary>
    /// <remarks>
    /// Null means unrated, which is a real and honest state: the call happened,
    /// the tokens are known, and this deployment has no price for that model. It
    /// does not mean free, and nothing may render it as zero.
    /// </remarks>
    public decimal? EstimatedCost { get; private set; }

    /// <summary>The currency the estimate is in. Null when unrated.</summary>
    public CurrencyCode? Currency { get; private set; }

    /// <summary>
    /// The rates this call was priced with, copied onto the row.
    /// </summary>
    /// <remarks>
    /// <strong>A snapshot, so history stays reproducible.</strong> Pointing at a
    /// catalogue entry would mean a price correction silently rewriting last
    /// quarter's costs. These four fields answer "what was this rated with?"
    /// without consulting anything that can change.
    /// </remarks>
    public decimal? InputRatePerMillion { get; private set; }

    public decimal? CachedInputRatePerMillion { get; private set; }

    public decimal? OutputRatePerMillion { get; private set; }

    public DateTimeOffset? RatesEffectiveFrom { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Whether this operation has been settled.</summary>
    public bool IsSettled => Status != AiOperationStatus.Started;

    /// <summary>Whether a cost could be put on it.</summary>
    public bool IsRated => EstimatedCost is not null;

    /// <summary>
    /// Records an operation about to be dispatched.
    /// </summary>
    /// <remarks>
    /// Called <em>before</em> the provider, never after. The row is the promise
    /// that money about to be spent will be accounted for.
    /// </remarks>
    public static AiProviderUsage Begin(
        TenantId tenantId,
        AiProvider provider,
        AiOperationType operationType,
        AiUsagePurpose purpose,
        ModelName requestedModel,
        DateTimeOffset startedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException(
                "A provider operation must be attributed to a workspace.");
        }

        if (requestedModel.IsEmpty)
        {
            throw new DomainValidationException(
                "A provider operation must record the model it asked for.");
        }

        return new AiProviderUsage(
            AiProviderUsageId.New(),
            tenantId,
            provider,
            operationType,
            purpose,
            requestedModel,
            RequireInstant(startedAt));
    }

    /// <summary>Rehydrates a stored operation. Persistence only.</summary>
    public static AiProviderUsage Restore(
        AiProviderUsageId id,
        TenantId tenantId,
        AiProvider provider,
        AiOperationType operationType,
        AiUsagePurpose purpose,
        ModelName requestedModel,
        ModelName? resolvedModel,
        AiOperationStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        ProviderTokenUsage usage,
        decimal? estimatedCost,
        CurrencyCode? currency,
        decimal? inputRatePerMillion,
        decimal? cachedInputRatePerMillion,
        decimal? outputRatePerMillion,
        DateTimeOffset? ratesEffectiveFrom,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return new AiProviderUsage(
            id,
            tenantId,
            provider,
            operationType,
            purpose,
            requestedModel,
            startedAt)
        {
            ResolvedModel = resolvedModel,
            Status = status,
            CompletedAt = completedAt,
            Usage = usage,
            EstimatedCost = estimatedCost,
            Currency = currency,
            InputRatePerMillion = inputRatePerMillion,
            CachedInputRatePerMillion = cachedInputRatePerMillion,
            OutputRatePerMillion = outputRatePerMillion,
            RatesEffectiveFrom = ratesEffectiveFrom,
        };
    }

    /// <summary>
    /// Records that the provider answered, with what it reported.
    /// </summary>
    /// <param name="rates">
    /// The rates to price it with, or <see langword="null"/> when this deployment
    /// has none for the model. Unrated is a state, not a failure.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when this operation was already settled, in which
    /// case nothing changed.
    /// </returns>
    public bool Succeed(
        ProviderTokenUsage usage,
        ModelName? resolvedModel,
        AiModelRates? rates,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (IsSettled)
        {
            // Settled once. A finalisation arriving twice must not double a cost,
            // and saying so lets the caller tell the difference between doing the
            // work and finding it already done.
            return false;
        }

        Usage = usage;
        ResolvedModel = resolvedModel;
        Status = AiOperationStatus.Succeeded;
        CompletedAt = RequireInstant(completedAt);

        ApplyRates(rates, usage);

        return true;
    }

    /// <summary>
    /// Records a definitive refusal. Nothing was produced.
    /// </summary>
    /// <returns><see langword="false"/> when it was already settled.</returns>
    public bool Fail(DateTimeOffset completedAt)
    {
        if (IsSettled)
        {
            return false;
        }

        Status = AiOperationStatus.Failed;
        CompletedAt = RequireInstant(completedAt);

        // No usage and no cost, because nothing was reported. That is not a claim
        // the call was free -- a provider that rejected a request generally does
        // not bill for it, and where it does, the absence here is honest about
        // this platform not knowing.
        return true;
    }

    /// <summary>
    /// Records that the outcome is unknown: dispatched, and then the line went
    /// quiet.
    /// </summary>
    /// <remarks>
    /// <strong>Not a failure, and never a zero.</strong> The provider may well
    /// have executed and billed this. It is recorded as unresolved so somebody
    /// reconciling an invoice can see exactly where the gap is.
    /// </remarks>
    /// <returns><see langword="false"/> when it was already settled.</returns>
    public bool MarkUnknown(DateTimeOffset completedAt)
    {
        if (IsSettled)
        {
            return false;
        }

        Status = AiOperationStatus.Unknown;
        CompletedAt = RequireInstant(completedAt);

        return true;
    }

    private void ApplyRates(AiModelRates? rates, ProviderTokenUsage usage)
    {
        if (rates is null)
        {
            return;
        }

        decimal? cost = rates.Rate(usage);

        if (cost is null)
        {
            // The catalogue had an entry but not every rate this call needed.
            // Left unrated rather than partially priced.
            return;
        }

        EstimatedCost = cost;
        Currency = rates.Currency;
        InputRatePerMillion = rates.InputPerMillion;
        CachedInputRatePerMillion = rates.CachedInputPerMillion;
        OutputRatePerMillion = rates.OutputPerMillion;
        RatesEffectiveFrom = rates.EffectiveFrom;
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant) =>
        instant == default
            ? throw new DomainValidationException("A usage instant must be a real point in time.")
            : instant.ToUniversalTime();
}
