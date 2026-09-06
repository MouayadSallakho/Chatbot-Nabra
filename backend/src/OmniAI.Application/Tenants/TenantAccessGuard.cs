using OmniAI.Application.Security;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tenants;

/// <summary>
/// Decides whether the current caller may touch a tenant's data.
/// </summary>
/// <remarks>
/// <strong>Every tenant-scoped use case goes through here, and that is the
/// point.</strong> An <c>[Authorize]</c> attribute on an endpoint proves somebody
/// is signed in; it says nothing about <em>which</em> tenant they may address,
/// and the tenant id arrives in the route where the caller put it. Relying on the
/// endpoint alone would mean any signed-in person could read any workspace whose
/// id they could guess or had once been shown. So the check lives in the
/// execution path, below HTTP, where no route mapping can forget it.
/// <para>
/// <strong>Two different failures, deliberately not merged.</strong> An anonymous
/// caller throws, because <c>401</c> is a fact about the request that the caller
/// can fix by signing in. A signed-in non-member gets <see langword="false"/>,
/// which each use case turns into its own ordinary not-found answer -- the same
/// answer a member gets for a tenant that never existed. That is what keeps
/// tenant ids from being probeable: nothing in the response distinguishes "this
/// workspace is not yours" from "there is no such workspace".
/// </para>
/// <para>
/// <strong>Not a general authorization framework.</strong> There is no policy
/// engine, no requirement handler, no permission catalogue and no role. One
/// question is asked -- does a membership row exist -- because one question is
/// what this phase authorises. Roles and permissions arrive with the phase that
/// introduces more than one kind of member.
/// </para>
/// <para>
/// A plain class rather than an interface: nothing substitutes it, and tests
/// exercise it against real fakes of its two dependencies. An interface here
/// would be a second name for the same thing, and would make it possible to
/// register a permissive implementation by accident.
/// </para>
/// </remarks>
public sealed class TenantAccessGuard(
    ICurrentUser currentUser,
    ITenantMembershipRepository memberships)
{
    private readonly ICurrentUser _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly ITenantMembershipRepository _memberships =
        memberships ?? throw new ArgumentNullException(nameof(memberships));

    /// <summary>
    /// The signed-in caller, or a refusal.
    /// </summary>
    /// <exception cref="NotAuthenticatedException">Nobody is signed in.</exception>
    /// <remarks>
    /// Used by the use cases that need the identity itself rather than a
    /// permission -- creating a workspace, listing the caller's own workspaces.
    /// </remarks>
    public UserId RequireCurrentUser() =>
        _currentUser.Id ?? throw new NotAuthenticatedException();

    /// <summary>
    /// Whether the signed-in caller belongs to this tenant.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a membership row pairs them with this tenant.
    /// <see langword="false"/> when it does not -- whether because the tenant
    /// belongs to somebody else or because it does not exist at all. The caller
    /// must not distinguish those two, and cannot: this method does not know
    /// which it was either.
    /// </returns>
    /// <exception cref="NotAuthenticatedException">
    /// Nobody is signed in. Thrown rather than answered <see langword="false"/>,
    /// so that an anonymous request cannot be reported as a missing resource.
    /// </exception>
    public async Task<bool> IsCurrentUserMemberAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        UserId userId = RequireCurrentUser();

        return await _memberships
            .ExistsAsync(userId, tenantId, cancellationToken)
            .ConfigureAwait(false);
    }
}
