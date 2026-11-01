using System.Globalization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Accounts;

/// <summary>One queued account email.</summary>
public readonly record struct AccountEmailId
{
    private AccountEmailId(Guid value) => Value = value;

    public Guid Value { get; }

    public static AccountEmailId New() => new(Guid.CreateVersion7());

    public static AccountEmailId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Account email id must not be an empty GUID.");
        }

        return new AccountEmailId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>What an account email is about.</summary>
public enum AccountEmailKind
{
    /// <summary>A link to set a new password. Its token is minted when it is sent.</summary>
    PasswordReset = 1,

    /// <summary>"Your password was changed." No link to act on and no token.</summary>
    PasswordChanged = 2,

    /// <summary>
    /// An invitation to a workspace, to somebody who may have no account yet. Its
    /// token is minted when it is sent; only the hash is kept, on the invitation.
    /// </summary>
    Invitation = 3,
}

/// <summary>Where one account email got to. The same states as an operational email.</summary>
public enum AccountEmailStatus
{
    Pending = 1,
    Sending = 2,
    Sent = 3,
    Failed = 4,
    Unknown = 5,
    Skipped = 6,
}

/// <summary>
/// An email about a person's account rather than about a workspace.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only who and when.</strong> The row holds the account (or, for an
/// invitation, the invitation and its workspace) and the time of the request. No address, token, link, subject or body is ever stored: the
/// address is read from the account and the message rendered when it is sent, and
/// a reset token is minted in memory at that moment and never written down.
/// </para>
/// <para>
/// <strong>Claimed and settled exactly like an operational email</strong>
/// (<c>FOR UPDATE SKIP LOCKED</c>, a claim token, a lease, and Unknown after a
/// stale Sending), because the same ambiguity applies: a message that may have
/// been handed over is never sent twice.
/// </para>
/// </remarks>
public sealed class AccountEmail
{
    public const int MaxFailureCodeLength = 40;

    private AccountEmail(
        AccountEmailId id,
        UserId? userId,
        TenantInvitationId? invitationId,
        TenantId? tenantId,
        AccountEmailKind kind,
        DateTimeOffset requestedAt)
    {
        Id = id;
        UserId = userId;
        InvitationId = invitationId;
        TenantId = tenantId;
        Kind = kind;
        RequestedAt = requestedAt;
        Status = AccountEmailStatus.Pending;
        AvailableAt = requestedAt;
    }

    public AccountEmailId Id { get; }

    /// <summary>The account, for account kinds. Exactly one of this and <see cref="InvitationId"/> is set.</summary>
    public UserId? UserId { get; }

    /// <summary>The invitation, for <see cref="AccountEmailKind.Invitation"/>.</summary>
    public TenantInvitationId? InvitationId { get; }

    /// <summary>The invitation's workspace, so the key into it is tenant-scoped.</summary>
    public TenantId? TenantId { get; }

    public AccountEmailKind Kind { get; }

    public DateTimeOffset RequestedAt { get; }

    public AccountEmailStatus Status { get; }

    public DateTimeOffset AvailableAt { get; }

    public int Attempts { get; }

    public DateTimeOffset? SentAt { get; }

    public DateTimeOffset? SettledAt { get; }

    public string? FailureCode { get; }

    public static AccountEmail ForUser(UserId userId, AccountEmailKind kind, DateTimeOffset requestedAt)
    {
        if (userId.IsEmpty)
        {
            throw new DomainValidationException("An account email must name an account.");
        }

        if (kind == AccountEmailKind.Invitation)
        {
            throw new DomainValidationException("An invitation email is addressed to an invitation, not an account.");
        }

        return new AccountEmail(AccountEmailId.New(), userId, null, null, kind, requestedAt.ToUniversalTime());
    }

    public static AccountEmail ForInvitation(TenantInvitation invitation, DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        return new AccountEmail(
            AccountEmailId.New(),
            null,
            invitation.Id,
            invitation.TenantId,
            AccountEmailKind.Invitation,
            requestedAt.ToUniversalTime());
    }
}
