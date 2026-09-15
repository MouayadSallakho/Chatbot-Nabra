using OmniAI.Domain.Ai;

namespace OmniAI.Application.Ai.Usage;

/// <summary>
/// Where provider spend is recorded.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Begin, then dispatch, then settle.</strong> The order is the whole
/// design. <see cref="BeginAsync"/> must have committed before a paid request
/// leaves the process, so that a crash, a lost connection or a failed settle can
/// never produce spend with no record of it. A provider adapter that cannot
/// begin must not call the provider.
/// </para>
/// <para>
/// <strong>Settling is idempotent.</strong> The same outcome applied twice
/// settles once. Cost is never counted twice, and concurrent callers are safe.
/// </para>
/// <para>
/// Rating happens here rather than at the call site: the ledger is the one place
/// that knows both the reported usage and the configured rates, and putting the
/// arithmetic anywhere else would invite a second, disagreeing implementation.
/// </para>
/// </remarks>
public interface IAiProviderUsageLedger
{
    /// <summary>
    /// Durably records an operation that is about to be dispatched.
    /// </summary>
    /// <remarks>
    /// Throws rather than returning a sentinel when it cannot commit. The caller
    /// is required to let that propagate without calling the provider — spending
    /// money the platform cannot account for is the one failure this design
    /// exists to prevent.
    /// </remarks>
    Task<AiProviderUsageId> BeginAsync(
        AiUsageContext context,
        AiProvider provider,
        AiOperationType operationType,
        ModelName requestedModel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the provider answered, with exactly what it reported.
    /// </summary>
    /// <param name="usage">
    /// The provider's own counts. Absent counts must stay absent: nothing here
    /// may be estimated from text.
    /// </param>
    Task SucceedAsync(
        AiProviderUsageId id,
        ProviderTokenUsage usage,
        ModelName? resolvedModel,
        CancellationToken cancellationToken = default);

    /// <summary>Records a definitive provider refusal.</summary>
    Task FailAsync(AiProviderUsageId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the request was dispatched and its outcome is unknown.
    /// </summary>
    /// <remarks>
    /// For a timeout or a dropped connection after dispatch. The provider may
    /// have executed and billed it, so this is neither a failure nor a zero.
    /// </remarks>
    Task MarkUnknownAsync(AiProviderUsageId id, CancellationToken cancellationToken = default);
}
