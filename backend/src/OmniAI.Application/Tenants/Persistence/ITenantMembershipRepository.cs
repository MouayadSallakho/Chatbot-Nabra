using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tenants.Persistence;

/// <summary>
/// The only way the use cases ask which tenants a person belongs to.
/// </summary>
/// <remarks>
/// Two methods, because two questions are actually asked: "may this person touch
/// this tenant" on every tenant-scoped request, and "which workspaces does this
/// person have" when the admin application starts. Nothing else is exposed --
/// there is no "list a tenant's members", because no authorised use case needs
/// one and publishing other people's memberships is exactly the kind of thing
/// that should require a phase of its own.
/// <para>
/// <strong>No <c>IQueryable</c>, no specification language, no generic
/// repository.</strong> Both would hand the caller the storage model back under
/// another name, and the second one would make it possible to write a membership
/// query that forgets its user predicate. Here the user is a required parameter
/// of every method, so there is no shape of call that reads across people.
/// </para>
/// <para>
/// <strong>Writes are absent on purpose.</strong> A membership is created in
/// exactly one place -- alongside the tenant it belongs to, in one transaction,
/// through <see cref="ITenantRepository.AddAsync"/>. An <c>AddAsync</c> here
/// would be a second way to grant access to a workspace, reachable without
/// creating one, and nothing in this phase needs it.
/// </para>
/// </remarks>
public interface ITenantMembershipRepository
{
    /// <summary>
    /// Whether this person belongs to this tenant.
    /// </summary>
    /// <remarks>
    /// <strong>Authoritative, and answered by the database on every call.</strong>
    /// Not cached, not derived from a claim, and not inferred from anything the
    /// browser sent. A claim baked into a cookie at sign-in would keep granting
    /// access after the membership was revoked, for as long as the cookie lived;
    /// a row lookup is correct the moment it is asked. The pair is the table's
    /// primary key, so this is a single index probe rather than a scan.
    /// </remarks>
    Task<bool> ExistsAsync(
        UserId userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenants this person belongs to, in a deterministic order.
    /// </summary>
    /// <returns>
    /// One summary per membership. A person with no memberships gets an empty
    /// list, which is an ordinary answer and not a failure -- it is exactly the
    /// state of somebody who has just registered.
    /// </returns>
    /// <remarks>
    /// Returns the tenants, not the membership rows: the caller wants to show a
    /// list of workspaces, and a list of <c>(userId, tenantId, createdAt)</c>
    /// triples would only have to be joined again. It exposes nobody else's
    /// membership -- the user is a required argument and the predicate is in the
    /// query.
    /// <para>
    /// The order is part of the contract rather than an implementation detail, so
    /// the workspace switcher does not reshuffle itself between page loads.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<TenantView>> ListTenantsAsync(
        UserId userId,
        CancellationToken cancellationToken = default);
}
