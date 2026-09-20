using OmniAI.Application.Authorization;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Plans;

/// <summary>Read one workspace's plan and how much of it is used.</summary>
public sealed record GetWorkspacePlanUsageQuery(Guid TenantId);

/// <summary>
/// The Plan and usage screen.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Behind <c>analytics.view</c>, and nothing new.</strong> This is the
/// same kind of fact the analytics dashboard already shows — how much of the
/// workspace is in use — and giving it a permission of its own would be a second
/// thing for an administrator to get right, exposing the same numbers when they
/// got it wrong. There is deliberately no <c>plan.view</c>, <c>quota.view</c> or
/// <c>billing.view</c>.
/// </para>
/// <para>
/// <strong>Authorisation before anything is counted.</strong> Somebody outside
/// the workspace must not learn that it holds five of five agents, so the
/// membership check runs before any count is taken and a non-member is told the
/// workspace is not there.
/// </para>
/// </remarks>
public sealed class GetWorkspacePlanUsageHandler(
    IWorkspaceEntitlementService entitlements,
    TenantAuthorizer authorizer)
{
    private readonly IWorkspaceEntitlementService _entitlements =
        entitlements ?? throw new ArgumentNullException(nameof(entitlements));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <returns>
    /// The snapshot, or <see langword="null"/> when the caller does not belong to
    /// this workspace.
    /// </returns>
    /// <exception cref="PermissionDeniedException">
    /// A member of the workspace who does not hold <c>analytics.view</c>.
    /// </exception>
    public async Task<WorkspaceEntitlements?> HandleAsync(
        GetWorkspacePlanUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.AnalyticsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        return await _entitlements.DescribeAsync(tenantId, cancellationToken).ConfigureAwait(false);
    }
}
