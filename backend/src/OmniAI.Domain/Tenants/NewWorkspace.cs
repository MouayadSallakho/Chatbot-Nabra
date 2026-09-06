using OmniAI.Domain.Authorization;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tenants;

/// <summary>
/// Everything a usable workspace consists of, built together.
/// </summary>
/// <param name="Tenant">The workspace itself.</param>
/// <param name="Membership">The membership that makes it reachable by its creator.</param>
/// <param name="OwnerRole">Its protected Owner role.</param>
/// <param name="OwnerAssignment">The assignment that gives the creator that role.</param>
/// <remarks>
/// <strong>Four rows, and a workspace missing any one of them is broken.</strong>
/// Without the membership the creator cannot reach the workspace at all; without
/// the Owner role there is no full authority in it and no way to create one, since
/// no caller can mint a system role; without the assignment the creator is a
/// member who may do nothing, in a workspace where nobody can grant them
/// anything. None of those states is recoverable through any endpoint this
/// platform publishes, so none of them may be reachable.
/// <para>
/// <strong>This type exists so that the four cannot be assembled wrongly.</strong>
/// The alternative was four arguments on a repository method, which would compile
/// perfectly well with a membership for one person and an assignment for another,
/// or with a role belonging to a different tenant. Here
/// <see cref="Create"/> is the only way to obtain one, it takes the two facts that
/// actually vary -- what the workspace is called and who is creating it -- and it
/// derives the rest. A mismatched set is not rejected; it is unrepresentable.
/// </para>
/// <para>
/// <strong>One instant for all four.</strong> The clock is read once by the
/// caller and passed in, so the membership, the role and the assignment carry the
/// timestamp of the single act that created them rather than three readings taken
/// microseconds apart.
/// </para>
/// <para>
/// <strong>It is a description, not a transaction.</strong> Building one writes
/// nothing. That the four rows are committed together or not at all is the
/// repository's promise, and its own tests prove it against PostgreSQL.
/// </para>
/// </remarks>
public sealed record NewWorkspace(
    Tenant Tenant,
    TenantMembership Membership,
    TenantRole OwnerRole,
    TenantRoleAssignment OwnerAssignment)
{
    /// <summary>
    /// Builds a workspace and everything that makes it usable by its creator.
    /// </summary>
    /// <param name="name">The workspace's display name.</param>
    /// <param name="slug">Its human-readable identifier.</param>
    /// <param name="creator">The person creating it, who becomes its Owner.</param>
    /// <param name="now">The instant, from the caller's injected clock.</param>
    /// <remarks>
    /// The creator becomes an Owner rather than a member with a chosen set of
    /// permissions, and that is not a default that could reasonably have been
    /// something else: somebody has to be able to administer a new workspace, and
    /// they are the only person in it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="slug"/> is null.</exception>
    /// <exception cref="DomainValidationException">The creator's identity is empty.</exception>
    public static NewWorkspace Create(
        TenantName name,
        TenantSlug slug,
        UserId creator,
        DateTimeOffset now)
    {
        Tenant tenant = Tenant.Create(name, slug);
        TenantRole owner = TenantRole.CreateOwner(tenant.Id, now);

        return new NewWorkspace(
            tenant,
            TenantMembership.Create(creator, tenant.Id, now),
            owner,
            TenantRoleAssignment.ForRole(creator, owner, now));
    }
}
