using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.ChangeTimeZone;

/// <summary>Changes the zone a workspace counts its days in.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="TimeZone">An IANA name, as the caller sent it.</param>
/// <param name="Expected">The version the caller read, from <c>If-Match</c>.</param>
public sealed record ChangeTenantTimeZoneCommand(Guid TenantId, string? TimeZone, ConcurrencyToken Expected);

/// <summary>
/// Sets the workspace time zone (Phase 37.6, row 34).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Order, as for the rename.</strong> The name is checked against the
/// runtime's zone database first (the same answer for anybody), then
/// authorisation (404 for a non-member, 403 for a member without
/// <c>workspace.manage</c>), then PostgreSQL's own zone list, and only then is
/// the workspace read and its version compared. Every refusal of the name is
/// <c>workspace.time_zone.invalid</c>.
/// </para>
/// <para>
/// <strong>What changes, and what does not.</strong> From the next read, analytics
/// days, report periods and schedule boundaries are local days of the new zone.
/// Reports already generated keep the zone they were computed in. Schedules still
/// to come move to the new zone's next boundary, in the same transaction; one
/// already due keeps its time. The plan quota's months stay in UTC.
/// </para>
/// <para>
/// <strong>Not a security event.</strong> Nothing about access or identity
/// changes, so no <c>security_events</c> row is written; choosing the current
/// zone again changes nothing and writes nothing.
/// </para>
/// </remarks>
public sealed class ChangeTenantTimeZoneHandler(
    ITenantRepository tenants,
    IWorkspaceTimeZones timeZones,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly IWorkspaceTimeZones _timeZones =
        timeZones ?? throw new ArgumentNullException(nameof(timeZones));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The workspace and its version, or <see langword="null"/> when the caller is
    /// not a member or the workspace does not exist.
    /// </returns>
    /// <exception cref="DomainValidationException">The zone is not one this platform accepts.</exception>
    /// <exception cref="PermissionDeniedException">A member without <c>workspace.manage</c>.</exception>
    /// <exception cref="TenantVersionMismatchException">The workspace changed since it was read.</exception>
    public async Task<Versioned<TenantView>?> HandleAsync(
        ChangeTenantTimeZoneCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        WorkspaceTimeZone zone = WorkspaceTimeZone.Create(command.TimeZone);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.WorkspaceManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        if (!await _timeZones.DatabaseKnowsAsync(zone, cancellationToken).ConfigureAwait(false))
        {
            throw new DomainValidationException(
                "Choose a time zone by its IANA name, such as Asia/Riyadh or Europe/Berlin.")
            {
                Code = WorkspaceTimeZone.InvalidCode,
            };
        }

        Versioned<Tenant>? stored = await _tenants
            .FindVersionedAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // Ordinal equality on an opaque value, before anything changes: a stale
        // tag is refused even when the change would have been a no-op.
        if (stored.Version != command.Expected)
        {
            throw TenantVersionMismatchException.ForTenant(tenantId.Value);
        }

        if (!stored.Value.ChangeTimeZone(zone))
        {
            return new Versioned<TenantView>(TenantView.From(stored.Value), stored.Version);
        }

        ConcurrencyToken version = await _tenants
            .ChangeTimeZoneAsync(stored.Value, command.Expected, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<TenantView>(TenantView.From(stored.Value), version);
    }
}
