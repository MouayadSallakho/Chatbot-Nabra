using OmniAI.Application.Concurrency;
using OmniAI.Application.Invitations;
using OmniAI.Application.Invitations.Persistence;
using OmniAI.Application.Members;
using OmniAI.Application.Members.Persistence;
using OmniAI.Application.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Team;

/// <summary>
/// An in-memory team store.
/// </summary>
/// <remarks>
/// It reproduces the properties the real repository is relied upon for: every
/// read is scoped to the tenant, role replacement is all-or-nothing, and the
/// membership version advances when the role set changes. A fake that ignored
/// any of those would let a handler defect through -- and the version one in
/// particular, because a precondition that never refuses anything is worse than
/// none.
/// </remarks>
internal sealed class InMemoryTeamRepository : ITenantMemberRepository
{
    private readonly Dictionary<(UserId, TenantId), TenantMemberRecord> _members = [];
    private readonly Dictionary<(UserId, TenantId), int> _versions = [];

    /// <summary>Puts somebody in a workspace.</summary>
    public InMemoryTeamRepository Add(
        UserId userId,
        TenantId tenantId,
        string email,
        params TenantRole[] roles)
    {
        _members[(userId, tenantId)] = new TenantMemberRecord(
            userId,
            EmailAddress.Create(email),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            roles);

        _versions[(userId, tenantId)] = 1;

        return this;
    }

    /// <summary>The roles somebody currently holds, for asserting a write landed.</summary>
    public IReadOnlyList<TenantRole> RolesOf(UserId userId, TenantId tenantId) =>
        _members.TryGetValue((userId, tenantId), out TenantMemberRecord? member)
            ? member.Roles
            : [];

    public Task<IReadOnlyList<TenantMemberRecord>> ListAsync(
        TenantId tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantMemberRecord>>(
        [
            .. _members
                .Where(entry => entry.Key.Item2 == tenantId)
                .Select(entry => entry.Value)
                .OrderBy(member => member.JoinedAt)
                .ThenBy(member => member.UserId.Value)
                .Skip(skip)
                .Take(take)
        ]);

    public Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.Count(entry => entry.Key.Item2 == tenantId));

    public Task<Versioned<TenantMemberRecord>?> FindAsync(
        TenantId tenantId,
        UserId userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _members.TryGetValue((userId, tenantId), out TenantMemberRecord? member)
                ? new Versioned<TenantMemberRecord>(member, VersionOf(userId, tenantId))
                : null);

    public Task<ConcurrencyToken> ReplaceRolesAsync(
        TenantId tenantId,
        UserId userId,
        IReadOnlyList<TenantRoleId> roleIds,
        ConcurrencyToken expected,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!_members.TryGetValue((userId, tenantId), out TenantMemberRecord? member))
        {
            throw TenantMemberVersionMismatchException.ForMember(userId.Value);
        }

        // Reproduces the database's own predicate, so a handler that stopped
        // comparing versions would still be caught here.
        if (expected != VersionOf(userId, tenantId))
        {
            throw TenantMemberVersionMismatchException.ForMember(userId.Value);
        }

        _members[(userId, tenantId)] = member with
        {
            Roles = [.. roleIds.Select(id => Roles.Single(role => role.Id == id))],
            AccessUpdatedAt = now,
        };

        _versions[(userId, tenantId)]++;

        return Task.FromResult(VersionOf(userId, tenantId));
    }

    /// <summary>Every role this double knows about, for resolving assignments.</summary>
    public List<TenantRole> Roles { get; } = [];

    private ConcurrencyToken VersionOf(UserId userId, TenantId tenantId) =>
        ConcurrencyToken.From($"v{_versions.GetValueOrDefault((userId, tenantId), 1)}");
}

/// <summary>An in-memory invitation store.</summary>
/// <remarks>
/// The one open invitation per address rule is reproduced here because it is a
/// business rule the use case relies on, even though the real enforcement is a
/// unique index. Expiring a stale invitation on reissue is reproduced for the
/// same reason.
/// </remarks>
internal sealed class InMemoryInvitationRepository : ITenantInvitationRepository
{
    private readonly List<TenantInvitation> _invitations = [];
    private readonly HashSet<(TenantId, string)> _members = [];

    /// <summary>Records that an address already belongs to a member.</summary>
    public InMemoryInvitationRepository WithMember(TenantId tenantId, string email)
    {
        _members.Add((tenantId, EmailAddress.Create(email).Canonical));

        return this;
    }

    /// <summary>Seeds an invitation without going through a use case.</summary>
    public TenantInvitation Seed(TenantInvitation invitation)
    {
        _invitations.Add(invitation);

        return invitation;
    }

    /// <summary>Every invitation this store holds, for assertions.</summary>
    public IReadOnlyList<TenantInvitation> All => _invitations;

    public Task AddAsync(
        TenantInvitation invitation,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        foreach (TenantInvitation stale in _invitations.Where(candidate =>
            candidate.TenantId == invitation.TenantId
            && candidate.Email.Matches(invitation.Email)
            && candidate.HasExpiredAt(now)))
        {
            stale.Expire(now);
        }

        if (_invitations.Exists(candidate =>
            candidate.TenantId == invitation.TenantId
            && candidate.Email.Matches(invitation.Email)
            && candidate.Status == TenantInvitationStatus.Pending))
        {
            throw new InvitationAlreadyOpenException();
        }

        _invitations.Add(invitation);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TenantInvitation>> ListAsync(
        TenantId tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantInvitation>>(
        [
            .. _invitations
                .Where(invitation => invitation.TenantId == tenantId)
                .OrderByDescending(invitation => invitation.CreatedAt)
                .Skip(skip)
                .Take(take)
        ]);

    public Task<int> CountAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_invitations.Count(invitation => invitation.TenantId == tenantId));

    public Task<TenantInvitation?> FindAsync(
        TenantId tenantId,
        TenantInvitationId invitationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_invitations.Find(invitation =>
            invitation.Id == invitationId && invitation.TenantId == tenantId));

    public Task<TenantInvitation?> FindByTokenHashAsync(
        InvitationTokenHash hash,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_invitations.Find(invitation => invitation.TokenHash == hash));

    public Task UpdateAsync(
        TenantInvitation invitation,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> AcceptAsync(
        TenantInvitation invitation,
        TenantMembership membership,
        IReadOnlyList<TenantRoleId> roleIds,
        UserId acceptedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        // The conditional write the real store performs, reproduced so a second
        // acceptance loses here rather than producing a duplicate membership.
        if (!invitation.IsUsableAt(now))
        {
            return Task.FromResult(false);
        }

        invitation.Accept(acceptedBy, now);
        Accepted.Add((membership, roleIds));

        return Task.FromResult(true);
    }

    /// <summary>What acceptance actually wrote, for asserting atomicity.</summary>
    public List<(TenantMembership Membership, IReadOnlyList<TenantRoleId> Roles)> Accepted { get; } =
        [];

    public Task<bool> IsAlreadyMemberAsync(
        TenantId tenantId,
        EmailAddress email,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.Contains((tenantId, email.Canonical)));
}

/// <summary>An account directory over a handful of known accounts.</summary>
internal sealed class InMemoryAccountDirectory : IAccountDirectory
{
    private readonly Dictionary<UserId, EmailAddress> _accounts = [];

    public InMemoryAccountDirectory With(UserId userId, string email)
    {
        _accounts[userId] = EmailAddress.Create(email);

        return this;
    }

    public Task<EmailAddress?> FindEmailAsync(
        UserId userId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _accounts.TryGetValue(userId, out EmailAddress email) ? email : (EmailAddress?)null);
}

/// <summary>
/// A token factory with no randomness, so a test can present the token it minted.
/// </summary>
/// <remarks>
/// Deliberately not the real implementation: these tests are about the use
/// cases' rules, and the cryptography has its own suite. What matters here is
/// that the same string hashes to the same digest, which is the property the
/// lookup depends on.
/// </remarks>
internal sealed class PredictableInvitationTokens : IInvitationTokens
{
    private int _issued;

    public NewInvitationToken Create()
    {
        string token = $"token-{++_issued}";

        return new NewInvitationToken(token, Digest(token));
    }

    public InvitationTokenHash? Recognise(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : Digest(token);

    private static InvitationTokenHash Digest(string token)
    {
        byte[] value = new byte[InvitationTokenHash.Length];

        System.Text.Encoding.ASCII.GetBytes(token).CopyTo(value, 0);

        return InvitationTokenHash.From(value);
    }
}
