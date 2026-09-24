using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Notifications;

/// <summary>
/// Who currently holds a permission in a workspace, in three bounded reads.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The same union rule as every other check.</strong> A member's
/// authority is the union of their roles' effective permissions — the Owner role's
/// included — computed from the same role definitions the request
/// path uses. There is no second definition of who may do what.
/// </para>
/// <para>
/// <strong>Current members only.</strong> Assignments are joined to memberships,
/// so somebody removed from the workspace is not a recipient even if a stale
/// assignment row were ever left behind.
/// </para>
/// </remarks>
internal sealed class NotificationDirectory(OmniAiDbContext context)
    : INotificationRecipientDirectory, INotificationSources
{
    private readonly OmniAiDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<UserId>> MembersHoldingAsync(
        TenantId tenantId,
        PermissionCode permission,
        CancellationToken cancellationToken = default)
    {
        var assignments = await _context.TenantRoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId)
            .Join(
                _context.TenantMemberships.AsNoTracking().Where(membership => membership.TenantId == tenantId),
                assignment => assignment.UserId,
                membership => membership.UserId,
                (assignment, _) => new { assignment.UserId, assignment.TenantRoleId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assignments.Count == 0)
        {
            return [];
        }

        List<TenantRole> roles = await _context.TenantRoles
            .AsNoTracking()
            .Include(role => role.Grants)
            .Where(role => role.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<TenantRoleId> granting =
        [
            .. roles
                .Where(role => role.EffectivePermissions.Contains(permission))
                .Select(role => role.Id),
        ];

        return
        [
            .. assignments
                .Where(assignment => granting.Contains(assignment.TenantRoleId))
                .Select(assignment => assignment.UserId)
                .Distinct()
                .OrderBy(user => user.Value),
        ];
    }

    public async Task<(UserId RequestedBy, ReportType Type, bool Scheduled)?> ReportAsync(
        TenantId tenantId,
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        ReportExecutionId id = ReportExecutionId.From(executionId);

        var execution = await _context.ReportExecutions
            .AsNoTracking()
            .Where(candidate => candidate.Id == id && candidate.TenantId == tenantId)
            .Select(candidate => new { candidate.RequestedBy, candidate.Type, candidate.ScheduleId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return execution is null ? null : (execution.RequestedBy, execution.Type, execution.ScheduleId is not null);
    }

    public async Task<Guid?> WhatsAppConnectionOfDeliveryAsync(
        TenantId tenantId,
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        WhatsAppDeliveryId id = WhatsAppDeliveryId.From(deliveryId);

        var connection = await _context.WhatsAppDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.Id == id && delivery.TenantId == tenantId)
            .Select(delivery => new { delivery.ConnectionId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return connection?.ConnectionId.Value;
    }
}
