using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OmniAI.Application.Invitations;
using OmniAI.Application.Invitations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Storing invitations, and settling the races between them.
/// </summary>
/// <remarks>
/// <strong>Three things here are decided by the database, not by this
/// code.</strong> Whether a second open invitation for one address can exist, who
/// wins when two people accept the same link at the same moment, and whether an
/// acceptance and a revocation can both succeed. Each is enforced by a constraint
/// or a conditional update, because a read followed by a write is a decision made
/// with information that was already stale when it arrived.
/// </remarks>
internal sealed class TenantInvitationRepository(OmniAiDbContext context)
    : ITenantInvitationRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task AddAsync(
        TenantInvitation invitation,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        // Closing the old one and opening the new one are a single decision. If
        // the first committed alone, a workspace would silently lose an invitation
        // nobody chose to withdraw.
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExpireStaleAsync(invitation, now, cancellationToken).ConfigureAwait(false);

        _context.TenantInvitations.Add(invitation);
        TenantInvitations.SetCanonicalEmail(_context, invitation);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsOpenInvitationViolation(failure))
        {
            // Two administrators inviting the same person at once both pass any
            // check-then-insert. The partial unique index is what makes exactly
            // one of them win.
            throw new InvitationAlreadyOpenException(
                new InvitationAlreadyOpenException().Message,
                failure);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TenantInvitation>> ListAsync(
        TenantId tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        await _context.TenantInvitations
            .AsNoTracking()
            .Include(invitation => invitation.Roles)
            .Where(invitation => invitation.TenantId == tenantId)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ThenBy(invitation => invitation.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        _context.TenantInvitations
            .AsNoTracking()
            .CountAsync(invitation => invitation.TenantId == tenantId, cancellationToken);

    public async Task<TenantInvitation?> FindAsync(
        TenantId tenantId,
        TenantInvitationId invitationId,
        CancellationToken cancellationToken = default) =>
        await _context.TenantInvitations
            .Include(invitation => invitation.Roles)
            .FirstOrDefaultAsync(
                invitation => invitation.Id == invitationId && invitation.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<TenantInvitation?> FindByTokenHashAsync(
        InvitationTokenHash hash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hash);

        // Compared as a captured value so the converter turns it into a single
        // bytea parameter. The digest is what travels to the database; the token
        // itself never appears in a query, a parameter log or a plan.
        InvitationTokenHash lookup = hash;

        return await _context.TenantInvitations
            .Include(invitation => invitation.Roles)
            .FirstOrDefaultAsync(
                invitation => invitation.TokenHash == lookup,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        TenantInvitation invitation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AcceptAsync(
        TenantInvitation invitation,
        TenantMembership membership,
        IReadOnlyList<TenantRoleId> roleIds,
        UserId acceptedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(roleIds);

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // The invitation is concluded first, with the pending state as part of the
        // predicate. Whichever transaction gets there first moves it out of
        // pending; every other acceptance, and any revocation, updates nothing and
        // is told so by the row count. That single conditional write is what makes
        // a duplicate membership impossible -- rather than a preceding read that
        // two callers would both pass.
        int concluded = await _context.TenantInvitations
            .Where(candidate =>
                candidate.Id == invitation.Id
                && candidate.Status == TenantInvitationStatus.Pending
                && candidate.ExpiresAt > now)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(candidate => candidate.Status, TenantInvitationStatus.Accepted)
                    .SetProperty(candidate => candidate.AcceptedAt, now)
                    .SetProperty(candidate => candidate.AcceptedByUserId, acceptedBy),
                cancellationToken)
            .ConfigureAwait(false);

        if (concluded == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }

        _context.TenantMemberships.Add(membership);

        foreach (TenantRoleId roleId in roleIds.Distinct())
        {
            _context.TenantRoleAssignments.Add(
                TenantRoleAssignment.Create(
                    membership.UserId,
                    membership.TenantId,
                    roleId,
                    now));
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // The membership or an assignment could not be written -- a duplicate
            // membership, or a role that has gone. Nothing is left behind: the
            // acceptance rolls back with it, so the invitation stays usable rather
            // than being marked used by somebody who never got in.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            throw;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public async Task<bool> IsAlreadyMemberAsync(
        TenantId tenantId,
        EmailAddress email,
        CancellationToken cancellationToken = default)
    {
        string canonical = email.Canonical;

        // Two queries rather than a join, because the membership's user id is a
        // value object behind a converter and Identity's is a bare Guid: a join
        // key would have to call a Domain factory inside the expression, which EF
        // cannot translate.
        //
        // The answer is still scoped to this workspace and is still only yes or
        // no. Nothing about accounts outside the tenant reaches the caller, and no
        // endpoint exposes this -- it exists so an administrator inviting somebody
        // already present is told so.
        List<Guid> memberIds = await _context.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId)
            .Select(membership => membership.UserId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _context.Users
            .AsNoTracking()
            .AnyAsync(
                user => memberIds.Contains(user.Id) && user.NormalizedEmail == canonical,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records that an earlier invitation to the same address has aged out.
    /// </summary>
    /// <remarks>
    /// <strong>This is what stops an expired invitation blocking its
    /// replacement.</strong> The open-invitation index is predicated on the stored
    /// status, because a partial index cannot consult the clock -- so an
    /// invitation that expired is still occupying the slot until something says
    /// otherwise. Issuing a replacement is exactly the moment that matters, and it
    /// happens inside the same transaction as the insert.
    /// <para>
    /// Only genuinely expired invitations are touched. One that is still live is
    /// left alone and the insert then fails on the index, which is the
    /// already-open answer the caller should get.
    /// </para>
    /// </remarks>
    private async Task ExpireStaleAsync(
        TenantInvitation invitation,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await _context.TenantInvitations
            .Where(candidate =>
                candidate.TenantId == invitation.TenantId
                && EF.Property<string>(candidate, PersistenceSchema.CanonicalEmail)
                    == invitation.Email.Canonical
                && candidate.Status == TenantInvitationStatus.Pending
                && candidate.ExpiresAt <= now)
            .ExecuteUpdateAsync(
                update => update.SetProperty(
                    candidate => candidate.Status,
                    TenantInvitationStatus.Expired),
                cancellationToken)
            .ConfigureAwait(false);

    private static bool IsOpenInvitationViolation(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && string.Equals(
            postgres.SqlState,
            PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(
            postgres.ConstraintName,
            "ux_tenant_invitations_open",
            StringComparison.Ordinal);
}
