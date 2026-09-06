using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Invitations.Persistence;

/// <summary>
/// Storing and finding invitations.
/// </summary>
/// <remarks>
/// <strong>Lookups are by digest, never by raw token.</strong> The Application
/// hashes what a caller presented and asks for a row matching that, so the secret
/// never travels into a query, a parameter log or a query plan.
/// </remarks>
public interface ITenantInvitationRepository
{
    /// <summary>
    /// Issues an invitation, closing any that has aged out for the same address.
    /// </summary>
    /// <remarks>
    /// <strong>One unit of work, because the two are one decision.</strong> A
    /// workspace may hold only one open invitation per address, enforced by a
    /// unique index. An invitation that expired is still recorded as pending until
    /// something marks it otherwise, so issuing a replacement means stamping the
    /// old one expired and inserting the new one together -- if the stamp
    /// committed and the insert failed, the workspace would have quietly lost an
    /// invitation nobody chose to withdraw.
    /// <para>
    /// The index decides races, not a preceding read: two administrators inviting
    /// the same person at once both pass any check-then-insert, and exactly one
    /// survives the constraint.
    /// </para>
    /// </remarks>
    /// <exception cref="InvitationAlreadyOpenException">
    /// An invitation for this address is already open.
    /// </exception>
    Task AddAsync(
        TenantInvitation invitation,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>One page of a workspace's invitations, newest first.</summary>
    /// <remarks>
    /// Newest first because a pending invitation somebody just created is the one
    /// they are looking for, unlike the member list where the workspace's history
    /// is the useful order.
    /// </remarks>
    Task<IReadOnlyList<TenantInvitation>> ListAsync(
        TenantId tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>How many invitations the workspace has, in any state.</summary>
    Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>One invitation of a workspace, by its id.</summary>
    /// <returns>
    /// <see langword="null"/> when this workspace has no such invitation, whether
    /// or not one exists elsewhere.
    /// </returns>
    Task<TenantInvitation?> FindAsync(
        TenantId tenantId,
        TenantInvitationId invitationId,
        CancellationToken cancellationToken = default);

    /// <summary>The invitation a presented token unlocks, wherever it lives.</summary>
    /// <remarks>
    /// Deliberately not scoped to a tenant: the token is what identifies the
    /// invitation, and the person accepting does not yet belong to the workspace
    /// it names. That is exactly why the token has to be unguessable.
    /// </remarks>
    Task<TenantInvitation?> FindByTokenHashAsync(
        InvitationTokenHash hash,
        CancellationToken cancellationToken = default);

    /// <summary>Saves a change to an invitation's state.</summary>
    Task UpdateAsync(TenantInvitation invitation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts an invitation and admits the account, in one transaction.
    /// </summary>
    /// <remarks>
    /// <strong>All of it or none of it.</strong> The membership, the role
    /// assignments and the invitation's acceptance are one fact about the world.
    /// A partial write would leave somebody a member with authority nobody
    /// granted, or an invitation marked used by a person who never got in.
    /// <para>
    /// The invitation's terminal state is what settles a race between two
    /// acceptances, or between an acceptance and a revocation: whichever
    /// transaction commits first has already moved it out of pending, and the
    /// other finds it no longer usable.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when this call performed the acceptance;
    /// <see langword="false"/> when somebody else concluded the invitation first.
    /// </returns>
    Task<bool> AcceptAsync(
        TenantInvitation invitation,
        TenantMembership membership,
        IReadOnlyList<Domain.Authorization.TenantRoleId> roleIds,
        UserId acceptedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the workspace already has a member signing in with this address.
    /// </summary>
    /// <remarks>
    /// Asked so an administrator inviting somebody already present is told so,
    /// rather than creating an invitation that could never usefully be accepted.
    /// <para>
    /// <strong>Scoped to one workspace, and answering only yes or no.</strong> It
    /// reports nothing about accounts outside this tenant, so it cannot become a
    /// way to discover who has registered on the platform. No endpoint exposes it.
    /// </para>
    /// </remarks>
    Task<bool> IsAlreadyMemberAsync(
        TenantId tenantId,
        EmailAddress email,
        CancellationToken cancellationToken = default);
}
