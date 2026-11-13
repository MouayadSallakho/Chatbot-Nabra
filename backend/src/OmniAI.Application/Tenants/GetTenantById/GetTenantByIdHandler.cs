using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants.GetTenantById;

/// <summary>
/// Reads one tenant the caller belongs to.
/// </summary>
/// <remarks>
/// <strong>The schedule count (Phase 38.7b, BA-9).</strong> For a caller who holds
/// <c>workspace.manage</c>, the read also says how many report schedules a
/// time-zone change would move, so the settings page can ask first without
/// <c>reports.view</c>. Everyone else gets no count at all, not a zero. The count is
/// not part of the workspace's version: the <c>ETag</c> still names the workspace
/// row a rename or a zone change must match.
/// </remarks>
public sealed class GetTenantByIdHandler(
    ITenantRepository tenants,
    TenantAuthorizer authorizer,
    TimeProvider clock)
{
    private readonly ITenantRepository _tenants =
        tenants ?? throw new ArgumentNullException(nameof(tenants));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Finds the tenant.</summary>
    /// <returns>
    /// The tenant's read model, or <see langword="null"/> when no tenant has that
    /// identity <em>or</em> the caller does not belong to it. Absence is a
    /// result, not a failure: it needs no exception type, produces no stack
    /// unwind and no error-level log, and the API maps it to its own not-found
    /// response.
    /// </returns>
    /// <remarks>
    /// <strong>Membership is checked before the tenant is read, and the two
    /// outcomes are deliberately identical.</strong> A caller who is not a member
    /// gets exactly what they would get for a tenant id that was never issued, so
    /// this endpoint cannot be used to discover which workspaces exist. Reading
    /// the tenant first and then filtering would produce the same status but
    /// would put somebody else's row into memory to do it, which is a needless
    /// place for a mistake to become a disclosure.
    /// </remarks>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="Domain.DomainValidationException">
    /// The supplied identifier is empty. Rejected by <see cref="TenantId.From"/>
    /// rather than treated as a lookup that happens to find nothing, because an
    /// empty GUID is malformed input rather than a tenant that does not exist.
    /// </exception>
    public async Task<Versioned<TenantView>?> HandleAsync(
        GetTenantByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId id = TenantId.From(query.TenantId);

        // Authorisation before the tenant is read, so a non-member never puts
        // somebody else's row into memory and a member without workspace.view is
        // refused without one either. One read of the caller's permissions answers
        // both questions this read asks: may they see the workspace, and may they
        // change its zone (Phase 38.7b).
        IReadOnlyList<PermissionCode>? held = await _authorizer
            .EffectivePermissionsAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (held is null)
        {
            return null;
        }

        if (!held.Contains(PlatformPermissions.WorkspaceView))
        {
            throw PermissionDeniedException.ForPermission(PlatformPermissions.WorkspaceView);
        }

        // Versioned since Phase 37.5a: the read now sends the ETag a rename must
        // name in If-Match.
        Versioned<Tenant>? tenant = await _tenants.FindVersionedAsync(id, cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            return null;
        }

        TenantView view = TenantView.From(tenant.Value);

        if (held.Contains(PlatformPermissions.WorkspaceManage))
        {
            view = view with
            {
                ScheduleCount = await _tenants
                    .CountSchedulesToRezoneAsync(id, _clock.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false),
            };
        }

        return new Versioned<TenantView>(view, tenant.Version);
    }
}
