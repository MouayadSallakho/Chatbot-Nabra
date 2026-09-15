using OmniAI.Application.Ai.Usage;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;

namespace OmniAI.Testing;

/// <summary>
/// A usage ledger that remembers rather than persists.
/// </summary>
/// <remarks>
/// <para>
/// It keeps the whole aggregate, so a test can assert on real Domain behaviour —
/// idempotent settling, rate application, status transitions — rather than on a
/// double's own bookkeeping.
/// </para>
/// <para>
/// <see cref="FailBegin"/> is the important switch. The cost-safety invariant is
/// that a ledger which cannot write stops the provider call, and the only way to
/// prove it is to make the ledger refuse and then check the transport saw
/// nothing.
/// </para>
/// </remarks>
public sealed class RecordingUsageLedger(IAiProviderPricing? pricing = null, TimeProvider? clock = null)
    : IAiProviderUsageLedger
{
    private readonly IAiProviderPricing? _pricing = pricing;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private readonly List<AiProviderUsage> _operations = [];

    private readonly Lock _gate = new();

    /// <summary>Every operation begun, in order.</summary>
    public IReadOnlyList<AiProviderUsage> Operations
    {
        get
        {
            lock (_gate)
            {
                return [.. _operations];
            }
        }
    }

    /// <summary>The only operation begun. Fails the test when there is not exactly one.</summary>
    public AiProviderUsage Only => Operations.Single();

    /// <summary>How many operations were begun.</summary>
    public int Count => Operations.Count;

    /// <summary>
    /// Makes the ledger refuse to record anything.
    /// </summary>
    /// <remarks>
    /// A deployment whose accounting store is unreachable. The provider must then
    /// receive nothing at all.
    /// </remarks>
    public bool FailBegin { get; set; }

    /// <summary>What was handed to the most recent begin.</summary>
    public AiUsageContext? LastContext { get; private set; }

    public Task<AiProviderUsageId> BeginAsync(
        AiUsageContext context,
        AiProvider provider,
        AiOperationType operationType,
        ModelName requestedModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (FailBegin)
        {
            throw new InvalidOperationException(
                "The usage ledger could not record this operation. Nothing was dispatched.");
        }

        AiProviderUsage operation = AiProviderUsage.Begin(
            context.TenantId,
            provider,
            operationType,
            context.Purpose,
            requestedModel,
            _clock.GetUtcNow());

        lock (_gate)
        {
            _operations.Add(operation);
        }

        LastContext = context;

        return Task.FromResult(operation.Id);
    }

    public Task SucceedAsync(
        AiProviderUsageId id,
        ProviderTokenUsage usage,
        ModelName? resolvedModel,
        CancellationToken cancellationToken = default)
    {
        AiProviderUsage? operation = Find(id);

        if (operation is null)
        {
            return Task.CompletedTask;
        }

        AiModelRates? rates = _pricing?.Find(
            operation.Provider,
            resolvedModel ?? operation.RequestedModel,
            operation.OperationType,
            operation.StartedAt);

        operation.Succeed(usage, resolvedModel, rates, _clock.GetUtcNow());

        return Task.CompletedTask;
    }

    public Task FailAsync(AiProviderUsageId id, CancellationToken cancellationToken = default)
    {
        Find(id)?.Fail(_clock.GetUtcNow());

        return Task.CompletedTask;
    }

    public Task MarkUnknownAsync(
        AiProviderUsageId id,
        CancellationToken cancellationToken = default)
    {
        Find(id)?.MarkUnknown(_clock.GetUtcNow());

        return Task.CompletedTask;
    }

    private AiProviderUsage? Find(AiProviderUsageId id)
    {
        lock (_gate)
        {
            return _operations.FirstOrDefault(operation => operation.Id == id);
        }
    }
}

/// <summary>Deterministic rates, for tests that care about money.</summary>
/// <remarks>
/// Invented numbers, chosen to be easy to verify by hand and obviously not any
/// real provider's price list. Nothing in this repository claims to know what
/// OpenAI charges.
/// </remarks>
public sealed class FixedPricing(params AiModelRates[] rates) : IAiProviderPricing
{
    private readonly AiModelRates[] _rates = rates ?? [];

    public AiModelRates? Find(
        AiProvider provider,
        ModelName model,
        AiOperationType operationType,
        DateTimeOffset at) =>
        _rates
            .Where(rate =>
                rate.Provider == provider
                && rate.Model == model
                && rate.Operation == operationType
                && rate.EffectiveFrom <= at)
            .OrderByDescending(rate => rate.EffectiveFrom)
            .FirstOrDefault();

    /// <summary>A deployment that has configured nothing. Everything is unrated.</summary>
    public static FixedPricing None { get; } = new();
}

/// <summary>Helpers for building usage context in tests.</summary>
public static class TestUsage
{
    /// <summary>A context for a workspace, defaulting to a conversation answer.</summary>
    public static AiUsageContext Context(
        TenantId? tenantId = null,
        AiUsagePurpose purpose = AiUsagePurpose.ConversationGeneration) =>
        AiUsageContext.For(tenantId ?? TenantId.New(), purpose);
}
