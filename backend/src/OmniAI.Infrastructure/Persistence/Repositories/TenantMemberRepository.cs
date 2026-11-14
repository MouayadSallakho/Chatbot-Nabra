using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Members;
using OmniAI.Application.Members.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The people in a workspace, and what they hold.
/// </summary>
/// <remarks>
/// <strong>Every query is scoped to the tenant, without exception.</strong> A
/// member is only ever read through the workspace they belong to, so a caller who
/// transposes a user id from elsewhere gets the same nothing as one who invents
/// it -- which is what stops this becoming a way to discover who has registered.
/// </remarks>
internal sealed class TenantMemberRepository(OmniAiDbContext context) : ITenantMemberRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<TenantMemberRecord>> ListAsync(
        TenantId tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        // Ordered before paging, and by a total order: join time is what an
        // administrator expects to see, and the user id breaks ties so two people
        // who joined in the same millisecond cannot swap places between pages.
        var rows = await _context.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId)
            .OrderBy(membership => membership.CreatedAt)
            .ThenBy(membership => membership.UserId)
            .Skip(skip)
            .Take(take)
            .Select(membership => new
            {
                membership.UserId,
                membership.CreatedAt,
                membership.UpdatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Addresses are fetched in a second query rather than joined in the
        // first. The membership's user id is a value object behind a converter
        // and Identity's is a bare Guid, so the join key would have to call a
        // Domain factory inside the expression -- which EF cannot translate, and
        // which fails at run time rather than at compile time.
        List<Guid> ids = [.. rows.Select(row => row.UserId.Value)];

        // Phase 39.1: the display name comes in the same query.
        Dictionary<Guid, (string? Email, string? Normalised, string? DisplayName)> accounts =
            await _context.Users
                .AsNoTracking()
                .Where(user => ids.Contains(user.Id))
                .Select(user => new { user.Id, user.Email, user.NormalizedEmail, user.DisplayName })
                .ToDictionaryAsync(
                    account => account.Id,
                    account => (account.Email, account.NormalizedEmail, account.DisplayName),
                    cancellationToken)
                .ConfigureAwait(false);

        List<TenantMemberRecord> members = [];

        foreach (var row in rows)
        {
            (string? email, string? normalised, string? displayName) =
                accounts.GetValueOrDefault(row.UserId.Value);

            members.Add(new TenantMemberRecord(
                row.UserId,
                ReadEmail(email, normalised),
                row.CreatedAt,
                row.UpdatedAt,
                await RolesOfAsync(tenantId, row.UserId, cancellationToken).ConfigureAwait(false),
                displayName));
        }

        return members;
    }

    public Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        _context.TenantMemberships
            .AsNoTracking()
            .CountAsync(membership => membership.TenantId == tenantId, cancellationToken);

    public async Task<Versioned<TenantMemberRecord>?> FindAsync(
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken = default)
    {
        TenantMembership? membership = await _context.TenantMemberships
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == tenantId && candidate.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (membership is null)
        {
            return null;
        }

        var account = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId.Value)
            .Select(user => new { user.Email, user.NormalizedEmail, user.DisplayName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        TenantMemberRecord record = new(
            userId,
            ReadEmail(account?.Email, account?.NormalizedEmail),
            membership.CreatedAt,
            membership.UpdatedAt,
            await RolesOfAsync(tenantId, userId, cancellationToken).ConfigureAwait(false),
            account?.DisplayName);

        return new Versioned<TenantMemberRecord>(record, CurrentVersion(membership));
    }

    public async Task<ConcurrencyToken> ReplaceRolesAsync(
        TenantId tenantId,
        UserId userId,
        IReadOnlyList<TenantRoleId> roleIds,
        ConcurrencyToken expected,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        TenantMembership? membership = await _context.TenantMemberships
            .FirstOrDefaultAsync(
                candidate => candidate.TenantId == tenantId && candidate.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (membership is null)
        {
            throw TenantMemberVersionMismatchException.ForMember(userId.Value);
        }

        PropertyEntry<TenantMembership, uint> version = RowVersion(membership);

        if (!RowVersionTokens.TryDecode(expected, out uint expectedRowVersion))
        {
            throw TenantMemberVersionMismatchException.ForMember(userId.Value);
        }

        // The version the caller presented becomes the predicate of the UPDATE, so
        // the database decides the race rather than a comparison this process made
        // a moment ago.
        version.OriginalValue = expectedRowVersion;

        // Removing the old assignments and writing the new ones is one decision.
        // A failure between the two would leave somebody holding a mixture of the
        // sets -- an access level nobody chose and nobody would think to look for.
        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        List<TenantRoleAssignment> existing = await _context.TenantRoleAssignments
            .Where(assignment =>
                assignment.TenantId == tenantId && assignment.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _context.TenantRoleAssignments.RemoveRange(existing);

        foreach (TenantRoleId roleId in roleIds.Distinct())
        {
            _context.TenantRoleAssignments.Add(
                TenantRoleAssignment.Create(userId, tenantId, roleId, now));
        }

        // The membership row is stamped so its version moves. Without this the
        // assignments would change while the parent's xmin stood still, and the
        // ETag a client had would still look current -- a precondition that never
        // refuses anything is worse than none, because it reads as protection.
        membership.MarkRolesChanged(now);
        _context.Entry(membership).Property(m => m.UpdatedAt).IsModified = true;

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException failure)
        {
            throw new TenantMemberVersionMismatchException(
                new TenantMemberVersionMismatchException().Message,
                failure);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return CurrentVersion(membership);
    }

    /// <summary>The roles a member currently holds, with their grants.</summary>
    /// <remarks>
    /// Grants are included because an authority decision is computed from what a
    /// role permits, and a role read without them would appear to grant nothing.
    /// </remarks>
    private async Task<IReadOnlyList<TenantRole>> RolesOfAsync(
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken) =>
        await _context.TenantRoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId && assignment.UserId == userId)
            .Join(
                _context.TenantRoles.AsNoTracking().Include(role => role.Grants),
                assignment => assignment.TenantRoleId,
                role => role.Id,
                (assignment, role) => role)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// An address from the account store, or a placeholder when it is unreadable.
    /// </summary>
    /// <remarks>
    /// An account row that has lost its address is a broken state, not a security
    /// question. The Team list still has to render, so it shows nothing rather
    /// than failing the whole page for one row -- and an empty address cannot
    /// match an invitation, so nothing downstream treats it as an identity.
    /// </remarks>
    private static EmailAddress ReadEmail(string? email, string? normalised) =>
        string.IsNullOrEmpty(email) || string.IsNullOrEmpty(normalised)
            ? default
            : EmailAddress.FromStored(email, normalised);

    private ConcurrencyToken CurrentVersion(TenantMembership membership) =>
        RowVersionTokens.Encode(RowVersion(membership).CurrentValue);

    private PropertyEntry<TenantMembership, uint> RowVersion(TenantMembership membership) =>
        _context.Entry(membership).Property<uint>(PersistenceSchema.XminConcurrencyToken);
}
