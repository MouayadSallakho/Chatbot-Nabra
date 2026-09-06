using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Members.Persistence;

/// <summary>
/// Reading and reassigning the people who belong to a workspace.
/// </summary>
/// <remarks>
/// <strong>Separate from <c>ITenantMembershipRepository</c> on purpose.</strong>
/// That interface answers the one question every authorised request asks --
/// does this person belong here -- and it is on the hot path of the entire
/// platform. This one serves the Team administration screens: paged reads that
/// join accounts and roles, and a write that replaces a role set. Merging them
/// would put administration queries behind the same door as the membership check
/// and invite somebody to reach for a heavyweight method in a hot path.
/// <para>
/// <strong>It returns a member's roles as aggregates.</strong> Every authority
/// decision in this phase is computed from what those roles permit, and only the
/// aggregate knows that -- an Owner permits the whole catalogue while storing no
/// grant rows at all. A repository that returned role names would force the use
/// cases to ask a second question to find out what they mean.
/// </para>
/// </remarks>
public interface ITenantMemberRepository
{
    /// <summary>One page of a workspace's members.</summary>
    /// <remarks>
    /// Ordered by when they joined, oldest first, then by user id to break ties.
    /// Join time is the order an administrator expects -- the workspace's creator
    /// at the top, newest arrival at the bottom -- and the tie-break makes the
    /// order total, so paging cannot show or skip a row because two people joined
    /// in the same millisecond.
    /// </remarks>
    Task<IReadOnlyList<TenantMemberRecord>> ListAsync(
        TenantId tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>How many members the workspace has.</summary>
    Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One member, with the version their role set is currently at.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when this workspace has no such member -- which is
    /// the same answer for an account that does not exist and one that exists
    /// elsewhere, so this cannot be used to discover who has registered.
    /// </returns>
    Task<Versioned<TenantMemberRecord>?> FindAsync(
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the roles a member holds, if their membership is still at the
    /// expected version.
    /// </summary>
    /// <remarks>
    /// <strong>One transaction, all or nothing.</strong> Removing the old
    /// assignments and writing the new ones happen together: a failure part way
    /// through must not leave somebody holding a mixture of the two sets, which
    /// would be an access level nobody chose.
    /// <para>
    /// The membership row's own version advances with it, so a change made
    /// entirely in the join table is still visible to the next caller's
    /// precondition.
    /// </para>
    /// </remarks>
    /// <returns>The membership's new version.</returns>
    /// <exception cref="TenantMemberVersionMismatchException">
    /// Somebody else changed this member's roles first.
    /// </exception>
    Task<ConcurrencyToken> ReplaceRolesAsync(
        TenantId tenantId,
        UserId userId,
        IReadOnlyList<TenantRoleId> roleIds,
        ConcurrencyToken expected,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
