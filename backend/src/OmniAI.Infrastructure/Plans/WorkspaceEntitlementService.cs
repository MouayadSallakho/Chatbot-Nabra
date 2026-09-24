using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OmniAI.Application.Plans;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Plans;

/// <summary>
/// Counts what a workspace holds, and refuses to let it hold more than its plan
/// allows.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The counts are the resources themselves.</strong> There is no counter
/// column and no rollup table: usage is a <c>COUNT</c> over <c>ai_agents</c>,
/// <c>knowledge_documents</c> and the rest. A cached counter is a second source
/// of truth that drifts the first time a delete path forgets to decrement it, and
/// the drift is invisible until somebody is wrongly refused — or wrongly allowed.
/// </para>
/// <para>
/// <strong>There are no soft deletes in this schema, which is why counting rows
/// is exactly right.</strong> A disabled agent, a draft endpoint and a suspended
/// connection are all still rows, so disabling something does not quietly free a
/// slot. Only genuine deletion does, which is the intended rule rather than a
/// happy accident.
/// </para>
/// <para>
/// <strong>Reservation is serialised in PostgreSQL, not in this process.</strong>
/// A <c>lock</c> statement here would protect one web server and nothing else.
/// <c>pg_advisory_xact_lock</c> is held by the database for the life of the
/// transaction and released when it ends — including when it ends by crashing,
/// which a manually released lock would not survive.
/// </para>
/// </remarks>
internal sealed class WorkspaceEntitlementService(
    OmniAiDbContext context,
    TimeProvider clock) : IWorkspaceEntitlementService
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<WorkspaceEntitlements> DescribeAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        Plan plan = await RequirePlanAsync(tenantId, cancellationToken).ConfigureAwait(false);

        DateTimeOffset assignedAt = await AssignedAtAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        EntitlementPeriod month = EntitlementPeriod.MonthOf(_clock.GetUtcNow());

        List<WorkspaceEntitlementView> views = [];

        foreach (EntitlementKind kind in Kinds)
        {
            long used = await CountAsync(tenantId, kind, month, cancellationToken)
                .ConfigureAwait(false);

            EntitlementUsage usage = EntitlementUsage.Create(kind, used, plan.LimitFor(kind));

            views.Add(new WorkspaceEntitlementView(
                usage.Kind,
                usage.Used,
                usage.Limit.Maximum,
                usage.Limit.IsUnlimited,
                usage.PercentUsed,
                usage.State,
                usage.Remaining,
                kind == EntitlementKind.MonthlyProviderOperations ? month : null));
        }

        return new WorkspaceEntitlements(
            plan.Code.Value,
            plan.DisplayName.Value,
            assignedAt,
            views);
    }

    public async Task<T> ReserveAsync<T>(
        TenantId tenantId,
        EntitlementKind kind,
        Func<CancellationToken, Task<T>> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        // A transaction may already be open when a repository owns one -- accepting
        // an invitation is the case that matters. Joining it is right: the lock,
        // the count and the insert all belong to the same atomic step, and opening
        // a second transaction would put them in different ones.
        IDbContextTransaction? existing = _context.Database.CurrentTransaction;

        IDbContextTransaction? owned = existing is null
            ? await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        try
        {
            await LockAsync(tenantId, kind, cancellationToken).ConfigureAwait(false);

            Plan plan = await RequirePlanAsync(tenantId, cancellationToken).ConfigureAwait(false);

            PlanLimit limit = plan.LimitFor(kind);

            EntitlementPeriod? period = kind == EntitlementKind.MonthlyProviderOperations
                ? EntitlementPeriod.MonthOf(_clock.GetUtcNow())
                : null;

            // Counted while the lock is held, so nothing can be added between the
            // count and the decision.
            long current = await CountAsync(tenantId, kind, period, cancellationToken)
                .ConfigureAwait(false);

            if (!limit.AllowsAnother(current))
            {
                throw EntitlementExceededException.For(kind, current, limit.Maximum!.Value, period);
            }

            T result = await mutate(cancellationToken).ConfigureAwait(false);

            await RecordThresholdAsync(tenantId, kind, limit, current, period, cancellationToken)
                .ConfigureAwait(false);

            if (owned is not null)
            {
                await owned.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch
        {
            if (owned is not null)
            {
                // Best effort: the transaction may already be unusable, and the
                // original failure is the one worth reporting.
                try
                {
                    await owned.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // Already completed or aborted. Nothing to undo.
                }
            }

            throw;
        }
        finally
        {
            if (owned is not null)
            {
                await owned.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Records a usage-threshold occurrence when this reservation crossed one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Phase 24's thresholds, not a second model.</strong> The state before
    /// and after this one operation are both computed by
    /// <see cref="EntitlementUsage"/> — Warning at 70%, Critical at 90%, Exhausted
    /// at the limit — and an occurrence is recorded only when the state moved up.
    /// Operations at 92% move nothing and record nothing.
    /// </para>
    /// <para>
    /// <strong>Exactly once per threshold per month.</strong> The crossing is
    /// decided under the reservation's advisory lock, so concurrent operations see
    /// each other's counts; and the occurrence key is the threshold and the UTC
    /// month, so even a repeat would be refused by the unique index. The ledger
    /// row and the occurrence commit in the same transaction.
    /// </para>
    /// </remarks>
    private async Task RecordThresholdAsync(
        TenantId tenantId,
        EntitlementKind kind,
        PlanLimit limit,
        long before,
        EntitlementPeriod? period,
        CancellationToken cancellationToken)
    {
        if (kind != EntitlementKind.MonthlyProviderOperations || limit.IsUnlimited || period is null)
        {
            return;
        }

        EntitlementState was = EntitlementUsage.Create(kind, before, limit).State;
        EntitlementState now = EntitlementUsage.Create(kind, before + 1, limit).State;

        NotificationKind? crossed = now > was
            ? now switch
            {
                EntitlementState.Warning => NotificationKind.UsageWarning,
                EntitlementState.Critical => NotificationKind.UsageCritical,
                EntitlementState.Exhausted => NotificationKind.UsageExhausted,
                _ => null,
            }
            : null;

        if (crossed is not NotificationKind notify)
        {
            return;
        }

        string month = period.From.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        await NotificationOccurrenceSql
            .RecordAsync(
                _context,
                NotificationOccurrence.Record(tenantId, notify, month, sourceId: null, _clock.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Serialises every reservation of one entitlement in one workspace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transaction-scoped, so it is released by the commit or the rollback and
    /// there is no path on which it leaks. The session-scoped variant would
    /// survive a failure and hold the workspace's slot until the connection went
    /// back to the pool and was reused.
    /// </para>
    /// <para>
    /// The key is (workspace, entitlement), so two workspaces never wait on each
    /// other and adding an agent never waits on adding a document.
    /// </para>
    /// </remarks>
    private async Task LockAsync(
        TenantId tenantId,
        EntitlementKind kind,
        CancellationToken cancellationToken) =>
        await _context.Database
            .ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})",
                [KeyFor(tenantId, kind)],
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// A stable 64-bit key for one workspace's entitlement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SHA-256 of the pair rather than <c>GetHashCode</c>: the managed hash is
    /// randomised per process, so two web servers would compute different keys for
    /// the same workspace and would not exclude each other at all.
    /// </para>
    /// <para>
    /// The literal prefix is a namespace, so an entitlement lock can never collide
    /// with another part of the platform that also locks on a workspace.
    /// </para>
    /// </remarks>
    private static long KeyFor(TenantId tenantId, EntitlementKind kind)
    {
        Span<byte> digest = stackalloc byte[32];

        SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"entitlement:{tenantId.Value:D}:{(int)kind}")),
            digest);

        return BitConverter.ToInt64(digest[..8]);
    }

    private async Task<Plan> RequirePlanAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        Plan? plan = await _context.WorkspacePlanAssignments
            .AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId)
            .Join(
                _context.Plans.AsNoTracking(),
                assignment => assignment.PlanId,
                candidate => candidate.Id,
                (_, candidate) => candidate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return plan ?? throw new WorkspacePlanMissingException(tenantId.Value);
    }

    private async Task<DateTimeOffset> AssignedAtAsync(
        TenantId tenantId,
        CancellationToken cancellationToken) =>
        await _context.WorkspacePlanAssignments
            .AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId)
            .Select(assignment => assignment.AssignedAt)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// How many of one entitlement a workspace currently holds.
    /// </summary>
    /// <remarks>
    /// Every branch is a server-side <c>COUNT</c> over one tenant-scoped table. A
    /// workspace with ten thousand documents costs the same here as one with ten.
    /// </remarks>
    private async Task<long> CountAsync(
        TenantId tenantId,
        EntitlementKind kind,
        EntitlementPeriod? period,
        CancellationToken cancellationToken) => kind switch
        {
            // A membership row is a seat. There is no revoked state -- removal
            // deletes the row -- so the count is exact. Pending invitations are
            // deliberately not counted: an invitation nobody accepted has cost the
            // workspace nothing, and counting it would let an administrator lock
            // their own team out by inviting people who never arrive.
            EntitlementKind.Members => await _context.TenantMemberships
                .CountAsync(membership => membership.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false),

            EntitlementKind.AiAgents => await _context.AiAgents
                .CountAsync(agent => agent.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false),

            // Manual and file-backed documents are the same row in the same table,
            // so they count identically -- which is the intended rule. Replacing a
            // document's file edits the existing row and therefore consumes
            // nothing further.
            EntitlementKind.KnowledgeDocuments => await _context.KnowledgeDocuments
                .CountAsync(document => document.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false),

            EntitlementKind.WebChatEndpoints => await _context.WebChatEndpoints
                .CountAsync(endpoint => endpoint.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false),

            EntitlementKind.WhatsAppConnections => await _context.WhatsAppConnections
                .CountAsync(connection => connection.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false),

            // Phase 23's ledger, unchanged and unduplicated. Every status counts:
            // Started, Succeeded, Failed and Unknown all mean a request reached the
            // paid-dispatch boundary, and the flows that never reach it -- rate
            // limits, duplicates, Human mode, an agent with no eligible knowledge,
            // an unconfigured provider -- write no row there and so cost nothing
            // here either.
            EntitlementKind.MonthlyProviderOperations => await Operations(tenantId, period)
                .CountAsync(cancellationToken)
                .ConfigureAwait(false),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entitlement."),
        };

    private IQueryable<AiProviderUsage> Operations(TenantId tenantId, EntitlementPeriod? period)
    {
        EntitlementPeriod month = period ?? EntitlementPeriod.MonthOf(_clock.GetUtcNow());

        return _context.AiProviderUsages
            .AsNoTracking()
            .Where(operation =>
                operation.TenantId == tenantId
                && operation.StartedAt >= month.From
                && operation.StartedAt < month.To);
    }

    /// <summary>Every entitlement, in the order a screen reads them.</summary>
    private static readonly EntitlementKind[] Kinds =
    [
        EntitlementKind.Members,
        EntitlementKind.AiAgents,
        EntitlementKind.KnowledgeDocuments,
        EntitlementKind.WebChatEndpoints,
        EntitlementKind.WhatsAppConnections,
        EntitlementKind.MonthlyProviderOperations,
    ];
}
