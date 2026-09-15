using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai.Usage;
using OmniAI.Domain.Ai;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Ai.Usage;

/// <summary>
/// Provider spend, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Begin commits its own transaction, alone.</strong> It cannot share one
/// with whatever the caller is doing, because the row has to survive
/// independently of it: an accounting record that rolled back with a failed
/// conversation write would leave spend unrecorded, which is the situation this
/// whole design exists to prevent.
/// </para>
/// <para>
/// <strong>Every settle is idempotent, and the aggregate is what makes it so.</strong>
/// Settling a settled operation changes nothing and writes nothing — a retried
/// handler or two threads racing on one response cannot double a cost. The
/// primary key makes a duplicate row impossible; the aggregate makes a duplicate
/// settle harmless.
/// </para>
/// <para>
/// <strong>Rating happens here, once, at the moment of success</strong>, using
/// the rates in force when the call was dispatched. The result is copied onto the
/// row, so a later price change cannot rewrite it.
/// </para>
/// <para>
/// A settle for an id that no longer exists is not an error. The row is the
/// authority; if it is gone, there is nothing to correct and nothing to be gained
/// from failing a caller who has already paid the provider.
/// </para>
/// <para>
/// <strong>Every operation opens its own scope and its own context.</strong>
/// Sharing the caller's would tie accounting to the caller's unit of work: a
/// <c>SaveChanges</c> here would flush whatever half-built conversation they had
/// pending, and their rollback would take the usage row with it. Independence is
/// the requirement, so it is arranged structurally rather than remembered.
/// </para>
/// </remarks>
internal sealed class AiProviderUsageLedger(
    IServiceScopeFactory scopes,
    IAiProviderPricing pricing,
    TimeProvider clock)
    : IAiProviderUsageLedger
{
    private readonly IServiceScopeFactory _scopes =
        scopes ?? throw new ArgumentNullException(nameof(scopes));

    private readonly IAiProviderPricing _pricing =
        pricing ?? throw new ArgumentNullException(nameof(pricing));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<AiProviderUsageId> BeginAsync(
        AiUsageContext context,
        AiProvider provider,
        AiOperationType operationType,
        ModelName requestedModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        AiProviderUsage operation = AiProviderUsage.Begin(
            context.TenantId,
            provider,
            operationType,
            context.Purpose,
            requestedModel,
            _clock.GetUtcNow());

        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        OmniAiDbContext database = scope.ServiceProvider.GetRequiredService<OmniAiDbContext>();

        database.AiProviderUsages.Add(operation);

        // Committed here, before the caller dispatches. If this throws, the
        // caller must not call the provider -- and it does not, because it lets
        // the exception through.
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return operation.Id;
    }

    public async Task SucceedAsync(
        AiProviderUsageId id,
        ProviderTokenUsage usage,
        ModelName? resolvedModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);

        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        OmniAiDbContext database = scope.ServiceProvider.GetRequiredService<OmniAiDbContext>();

        AiProviderUsage? operation = await FindAsync(database, id, cancellationToken)
            .ConfigureAwait(false);

        if (operation is null)
        {
            return;
        }

        // Priced with what was in force when the call was dispatched, not now.
        // A call made last quarter stays rated at last quarter's prices however
        // many times the catalogue has changed since.
        AiModelRates? rates = _pricing.Find(
            operation.Provider,
            resolvedModel ?? operation.RequestedModel,
            operation.OperationType,
            operation.StartedAt);

        if (!operation.Succeed(usage, resolvedModel, rates, _clock.GetUtcNow()))
        {
            // Already settled. Nothing written, nothing doubled.
            return;
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task FailAsync(AiProviderUsageId id, CancellationToken cancellationToken = default) =>
        SettleAsync(id, (operation, at) => operation.Fail(at), cancellationToken);

    public Task MarkUnknownAsync(
        AiProviderUsageId id,
        CancellationToken cancellationToken = default) =>
        SettleAsync(id, (operation, at) => operation.MarkUnknown(at), cancellationToken);

    private async Task SettleAsync(
        AiProviderUsageId id,
        Func<AiProviderUsage, DateTimeOffset, bool> settle,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();

        OmniAiDbContext database = scope.ServiceProvider.GetRequiredService<OmniAiDbContext>();

        AiProviderUsage? operation = await FindAsync(database, id, cancellationToken)
            .ConfigureAwait(false);

        if (operation is null || !settle(operation, _clock.GetUtcNow()))
        {
            return;
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AiProviderUsage?> FindAsync(
        OmniAiDbContext database,
        AiProviderUsageId id,
        CancellationToken cancellationToken) =>
        await database.AiProviderUsages
            .FirstOrDefaultAsync(operation => operation.Id == id, cancellationToken)
            .ConfigureAwait(false);
}
