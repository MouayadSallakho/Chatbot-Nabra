using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Invitations;

/// <summary>
/// An offer of membership in one workspace, to one email address.
/// </summary>
/// <remarks>
/// <strong>What an invitation actually is: a bearer secret with an intent
/// attached.</strong> Anybody holding the token can present it; what stops the
/// wrong person using it is that acceptance also requires being signed in as the
/// address it names. Both halves are necessary. The token alone would let anyone
/// who saw the link join, and the address alone would let anybody who knew a
/// colleague's email join without being invited.
/// <para>
/// <strong>The intent is not a promise.</strong> The roles recorded here are what
/// the inviter meant to grant at the moment they wrote it. Nothing about that is
/// authoritative later: by the time somebody accepts, the inviter may have lost
/// the authority to grant those roles, or the roles themselves may have grown
/// beyond what the inviter could ever have delegated. Acceptance therefore
/// re-checks the whole picture against the present, and this aggregate holds no
/// cached permission set that could be trusted instead. See the acceptance use
/// case, which is where that check lives.
/// </para>
/// <para>
/// <strong>Immutable except for its ending.</strong> An invitation is written
/// once and thereafter only concludes -- accepted, revoked, or expired. There is
/// no editing the address, the roles or the expiry, because every one of those is
/// something the recipient may already have been told. Changing an invitation
/// after sharing its link would mean the link now does something other than what
/// was described. A different intent is a different invitation.
/// </para>
/// <para>
/// <strong>It holds a digest, never the token.</strong> The secret exists exactly
/// once, in the response to the request that created it. This aggregate is
/// constructed from the hash; there is no property, field or constructor
/// parameter anywhere on it that could hold the raw value, so no code path can
/// log, serialise or store one.
/// </para>
/// </remarks>
public sealed class TenantInvitation
{
    private readonly List<InvitationRoleGrant> _roles = [];

    private TenantInvitation(
        TenantInvitationId id,
        TenantId tenantId,
        EmailAddress email,
        UserId invitedByUserId,
        InvitationTokenHash tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        TenantId = tenantId;
        Email = email;
        InvitedByUserId = invitedByUserId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Status = TenantInvitationStatus.Pending;
    }

    /// <summary>The invitation's identity. Not its secret.</summary>
    public TenantInvitationId Id { get; }

    /// <summary>The workspace being joined. Fixed for the invitation's life.</summary>
    public TenantId TenantId { get; }

    /// <summary>The address invited, and the only account that may accept.</summary>
    public EmailAddress Email { get; }

    /// <summary>Who issued it.</summary>
    /// <remarks>
    /// Kept because acceptance re-checks that this person is still a member and
    /// still entitled to have invited, not merely so the list can show a name. An
    /// invitation must not outlive the authority that issued it.
    /// </remarks>
    public UserId InvitedByUserId { get; }

    /// <summary>The digest of the token that accepts this invitation.</summary>
    public InvitationTokenHash TokenHash { get; }

    /// <summary>When it was issued.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>The instant after which it can no longer be accepted.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Its recorded state.</summary>
    /// <remarks>
    /// <see cref="TenantInvitationStatus.Pending"/> here does not mean usable --
    /// ask <see cref="IsUsableAt"/>, which also weighs the clock.
    /// </remarks>
    public TenantInvitationStatus Status { get; private set; }

    /// <summary>When it was accepted, if it was.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>Which account accepted it, if any.</summary>
    /// <remarks>
    /// Recorded separately from <see cref="Email"/> because they answer different
    /// questions: the address is who was invited, and this is the account that
    /// actually arrived. They will match -- acceptance enforces it -- and keeping
    /// both means that can be audited rather than assumed.
    /// </remarks>
    public UserId? AcceptedByUserId { get; private set; }

    /// <summary>When it was revoked, if it was.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The roles it intends to grant. May be empty.</summary>
    public IReadOnlyCollection<InvitationRoleGrant> Roles => _roles;

    /// <summary>The ids of those roles.</summary>
    public IReadOnlyList<TenantRoleId> RoleIds => [.. _roles.Select(role => role.RoleId)];

    /// <summary>Issues an invitation.</summary>
    /// <remarks>
    /// <para>
    /// <strong>An invitation granting nothing is legitimate and deliberately
    /// allowed.</strong> Somebody who may invite but may not hand out authority
    /// can still bring a colleague into the workspace, where an administrator who
    /// does hold that authority gives them access. Refusing it would push people
    /// towards asking for the broader permission instead.
    /// </para>
    /// <para>
    /// Duplicate role ids are collapsed rather than refused: asking for the same
    /// role twice expresses the same intent as asking once, and the stored result
    /// is identical either way.
    /// </para>
    /// </remarks>
    /// <exception cref="DomainValidationException">
    /// An identifier is empty, or the lifetime does not end after it begins.
    /// </exception>
    public static TenantInvitation Issue(
        TenantId tenantId,
        EmailAddress email,
        UserId invitedByUserId,
        InvitationTokenHash tokenHash,
        IEnumerable<TenantRoleId> roleIds,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(roleIds);

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("An invitation must name a tenant.");
        }

        if (email.IsEmpty)
        {
            throw new DomainValidationException("An invitation must name an email address.");
        }

        if (invitedByUserId.IsEmpty)
        {
            throw new DomainValidationException("An invitation must name who issued it.");
        }

        if (expiresAt <= createdAt)
        {
            throw new DomainValidationException(
                "An invitation must expire after it was created.");
        }

        TenantInvitation invitation = new(
            TenantInvitationId.New(),
            tenantId,
            email,
            invitedByUserId,
            tokenHash,
            createdAt,
            expiresAt);

        foreach (TenantRoleId roleId in roleIds.Distinct())
        {
            if (roleId.IsEmpty)
            {
                throw new DomainValidationException("An invitation role must name a role.");
            }

            invitation._roles.Add(InvitationRoleGrant.For(invitation.Id, tenantId, roleId));
        }

        return invitation;
    }

    /// <summary>Whether this invitation can still be accepted at a given instant.</summary>
    /// <remarks>
    /// The only question worth asking about an invitation's state, and the reason
    /// <see cref="Status"/> alone is never enough: a row still marked pending is
    /// unusable the moment the clock passes <see cref="ExpiresAt"/>, whether or
    /// not anything has got round to recording that.
    /// </remarks>
    public bool IsUsableAt(DateTimeOffset now) =>
        Status == TenantInvitationStatus.Pending && now < ExpiresAt;

    /// <summary>Whether it has aged out, whatever the stored status says.</summary>
    public bool HasExpiredAt(DateTimeOffset now) =>
        Status == TenantInvitationStatus.Pending && now >= ExpiresAt;

    /// <summary>Marks it accepted by an account.</summary>
    /// <remarks>
    /// <strong>This does not decide whether acceptance was allowed.</strong> Every
    /// security question -- does the address match, is the inviter still entitled,
    /// have the roles outgrown what could be delegated -- is settled by the use
    /// case before this is called, because those questions need the workspace's
    /// current state and an aggregate can only see itself. What this guarantees is
    /// narrower and still worth having: an invitation cannot be accepted twice,
    /// and cannot be accepted after it was revoked or after it aged out.
    /// </remarks>
    /// <exception cref="DomainStateException">It is no longer usable.</exception>
    public void Accept(UserId acceptedByUserId, DateTimeOffset now)
    {
        if (acceptedByUserId.IsEmpty)
        {
            throw new DomainValidationException("An acceptance must name the account accepting.");
        }

        if (!IsUsableAt(now))
        {
            throw new DomainStateException(
                "This invitation can no longer be accepted.");
        }

        Status = TenantInvitationStatus.Accepted;
        AcceptedAt = now;
        AcceptedByUserId = acceptedByUserId;
    }

    /// <summary>Withdraws it.</summary>
    /// <remarks>
    /// Permitted on an invitation that has aged out but not yet been recorded as
    /// expired: an administrator looking at a stale-looking entry and choosing to
    /// close it should not be told the operation is invalid. Refused once it has
    /// genuinely concluded, so revoking cannot undo an acceptance.
    /// </remarks>
    /// <exception cref="DomainStateException">It has already concluded.</exception>
    public void Revoke(DateTimeOffset now)
    {
        if (Status != TenantInvitationStatus.Pending)
        {
            throw new DomainStateException(
                Status == TenantInvitationStatus.Accepted
                    ? "This invitation has already been accepted and cannot be revoked."
                    : "This invitation has already been closed.");
        }

        Status = TenantInvitationStatus.Revoked;
        RevokedAt = now;
    }

    /// <summary>Records that it aged out.</summary>
    /// <remarks>
    /// Called when a replacement is issued for the same address, so the
    /// open-invitation index has room for it. It changes nothing about whether the
    /// invitation could be used -- it could not, from the instant it expired --
    /// only what the row says.
    /// </remarks>
    /// <exception cref="DomainStateException">It has not aged out.</exception>
    public void Expire(DateTimeOffset now)
    {
        if (!HasExpiredAt(now))
        {
            throw new DomainStateException(
                "This invitation has not expired and must not be recorded as expired.");
        }

        Status = TenantInvitationStatus.Expired;
    }
}
