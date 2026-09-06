using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Members.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Members.GetTenantMemberById;

/// <summary>One member of one workspace.</summary>
/// <param name="TenantId">The workspace.</param>
/// <param name="UserId">The member.</param>
public sealed record GetTenantMemberByIdQuery(Guid TenantId, Guid UserId);

/// <summary>
/// Reads one member and the version their access is at.
/// </summary>
/// <remarks>
/// <strong>Reading is <c>members.view</c>; changing is a separate
/// permission.</strong> Somebody who may see the team but not reassign it gets
/// the same record, and the Team screen renders it read-only. The version travels
/// regardless, because whether the caller can use it is not this query's
/// business.
/// </remarks>
public sealed class GetTenantMemberByIdHandler(
    ITenantMemberRepository members,
    TenantAuthorizer authorizer)
{
    private readonly ITenantMemberRepository _members =
        members ?? throw new ArgumentNullException(nameof(members));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <summary>Reads the member.</summary>
    /// <returns>
    /// <see langword="null"/> when the caller does not belong to this workspace,
    /// or when this workspace has no such member. The two are deliberately the
    /// same answer: distinguishing them would let a member of one workspace
    /// discover who has registered on the platform by trying user ids.
    /// </returns>
    /// <exception cref="Security.NotAuthenticatedException">Nobody is signed in.</exception>
    /// <exception cref="PermissionDeniedException">
    /// The caller is a member without <c>members.view</c>.
    /// </exception>
    public async Task<Versioned<TenantMemberView>?> HandleAsync(
        GetTenantMemberByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        UserId userId = UserId.From(query.UserId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.MembersView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<TenantMemberRecord>? stored = await _members
            .FindAsync(tenantId, userId, cancellationToken)
            .ConfigureAwait(false);

        return stored is null
            ? null
            : new Versioned<TenantMemberView>(
                TenantMembers.Describe(stored.Value, tenantId.Value),
                stored.Version);
    }
}
