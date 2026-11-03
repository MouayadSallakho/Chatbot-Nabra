using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tenants.RenameTenant;

/// <summary>Renames a workspace.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="Name">The new display name, as the caller typed it.</param>
/// <param name="Expected">The version the caller read, from <c>If-Match</c>.</param>
public sealed record RenameTenantCommand(Guid TenantId, string? Name, ConcurrencyToken Expected);

/// <summary>
/// Renames a workspace (Phase 37.5a, row 20a, owner decision D-3).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists now.</strong> D-3 put the workspace's name on the public
/// chat reads, so the owner has to control what visitors see. The name follows the
/// rules it was created under -- required, at most 200 characters, no control
/// characters -- and a refusal is <c>workspace.name.invalid</c>.
/// </para>
/// <para>
/// <strong>Order, as for every conditional write here.</strong> The name is
/// checked first (the same answer for anybody), then authorisation (404 for a
/// non-member, 403 for a member without <c>workspace.manage</c>), and only then
/// is the workspace read and its version compared. A valid tag for somebody
/// else's workspace buys nothing.
/// </para>
/// <para>
/// <strong>Audited, never with the names.</strong> Each real rename writes one
/// <c>workspace.renamed</c> security event with the actor, the workspace and the
/// time, in the same transaction as the rename. The event table holds no free
/// text, so neither the old nor the new name is in it. Renaming to the current
/// name changes nothing and records nothing.
/// </para>
/// </remarks>
public sealed class RenameTenantHandler(
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

    /// <returns>
    /// The workspace and its new version, or <see langword="null"/> when the
    /// caller is not a member or the workspace does not exist.
    /// </returns>
    /// <exception cref="Domain.DomainValidationException">The name is not acceptable.</exception>
    /// <exception cref="PermissionDeniedException">A member without <c>workspace.manage</c>.</exception>
    /// <exception cref="TenantVersionMismatchException">The workspace changed since it was read.</exception>
    public async Task<Versioned<TenantView>?> HandleAsync(
        RenameTenantCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        TenantName name = TenantName.Create(command.Name);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.WorkspaceManage, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<Tenant>? stored = await _tenants
            .FindVersionedAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        // Ordinal equality on an opaque value, before anything changes: a stale
        // tag is refused even when the rename would have been a no-op.
        if (stored.Version != command.Expected)
        {
            throw TenantVersionMismatchException.ForTenant(tenantId.Value);
        }

        if (stored.Value.Name == name)
        {
            return new Versioned<TenantView>(TenantView.From(stored.Value), stored.Version);
        }

        UserId actor = _authorizer.RequireCurrentUser();

        stored.Value.Rename(name);

        SecurityEvent audit = SecurityEvent.Record(
            SecurityEventKinds.WorkspaceRenamed,
            _clock.GetUtcNow(),
            userId: actor,
            tenantId: tenantId);

        ConcurrencyToken version = await _tenants
            .RenameAsync(stored.Value, command.Expected, audit, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<TenantView>(TenantView.From(stored.Value), version);
    }
}
