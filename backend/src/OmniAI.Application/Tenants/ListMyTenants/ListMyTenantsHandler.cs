using OmniAI.Application.Security;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tenants.ListMyTenants;

/// <summary>
/// Lists the workspaces the signed-in person belongs to.
/// </summary>
/// <remarks>
/// <strong>This is the authoritative list of tenant ids the admin application may
/// use.</strong> The browser remembers which workspace was last selected, but it
/// does not get to invent one: every id the frontend offers came from here, and
/// every tenant-scoped request is checked against membership again anyway. The
/// list is a convenience for the UI, not the boundary -- editing it in the
/// browser buys nothing.
/// <para>
/// <strong>No parameters, and no query object.</strong> The one input is who is
/// asking, and that is not something a caller may supply -- accepting a user id
/// argument would create a method whose whole security depends on every call site
/// passing the right one. It is read from <see cref="ICurrentUser"/> instead, so
/// there is no way to ask this question about somebody else.
/// </para>
/// <para>
/// No paging. A person belongs to the workspaces they created, which today is a
/// handful; a page parameter would be a contract with nothing behind it. If
/// invitations ever make this list long, paging arrives with them.
/// </para>
/// </remarks>
public sealed class ListMyTenantsHandler(
    TenantAccessGuard access,
    ITenantMembershipRepository memberships)
{
    private readonly TenantAccessGuard _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly ITenantMembershipRepository _memberships =
        memberships ?? throw new ArgumentNullException(nameof(memberships));

    /// <summary>Reads the caller's workspaces.</summary>
    /// <returns>
    /// The tenants they belong to, newest membership first. Empty for somebody
    /// who has just registered, which is an ordinary state the admin application
    /// handles by offering to create a workspace.
    /// </returns>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    public async Task<IReadOnlyList<TenantView>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        UserId userId = _access.RequireCurrentUser();

        return await _memberships
            .ListTenantsAsync(userId, cancellationToken)
            .ConfigureAwait(false);
    }
}
