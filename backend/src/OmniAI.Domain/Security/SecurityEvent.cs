using System.Globalization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Security;

/// <summary>One audit record of a security-relevant event.</summary>
public readonly record struct SecurityEventId
{
    private SecurityEventId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SecurityEventId New() => new(Guid.CreateVersion7());

    public static SecurityEventId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Security event id must not be an empty GUID.");
        }

        return new SecurityEventId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// An audit record: what happened, when, to whom, and from which network.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing that could be replayed or read back.</strong> No address, no
/// token, no password, no link and no free text. A request that names an address
/// is recorded by <see cref="SubjectKey"/>, a keyed hash of the normalised
/// address, so attempts against one address can be counted together without the
/// table ever holding it. The client's network is the same keyed hash the rate
/// limits use.
/// </para>
/// <para>
/// <strong>It outlives what it names.</strong> Deleting the account or the
/// workspace clears the reference and keeps the record.
/// </para>
/// </remarks>
public sealed class SecurityEvent
{
    /// <summary>The longest kind, for the column.</summary>
    public const int MaxKindLength = 64;

    /// <summary>The longest keyed hash, for the column.</summary>
    public const int MaxKeyLength = 32;

    private SecurityEvent(
        SecurityEventId id,
        DateTimeOffset occurredAt,
        string kind,
        UserId? userId,
        TenantId? tenantId,
        string? subjectKey,
        string? clientNetworkKey)
    {
        Id = id;
        OccurredAt = occurredAt;
        Kind = kind;
        UserId = userId;
        TenantId = tenantId;
        SubjectKey = subjectKey;
        ClientNetworkKey = clientNetworkKey;
    }

    public SecurityEventId Id { get; }

    public DateTimeOffset OccurredAt { get; }

    /// <summary>One of <see cref="SecurityEventKinds"/>.</summary>
    public string Kind { get; }

    /// <summary>The account concerned, when one is known.</summary>
    public UserId? UserId { get; }

    /// <summary>The workspace concerned, for workspace-scoped events; empty for account events.</summary>
    public TenantId? TenantId { get; }

    /// <summary>The keyed hash of the address a request named.</summary>
    public string? SubjectKey { get; }

    /// <summary>The keyed hash of the client's network.</summary>
    public string? ClientNetworkKey { get; }

    public static SecurityEvent Record(
        string kind,
        DateTimeOffset occurredAt,
        UserId? userId = null,
        TenantId? tenantId = null,
        string? subjectKey = null,
        string? clientNetworkKey = null)
    {
        if (!SecurityEventKinds.IsKnown(kind))
        {
            throw new DomainValidationException($"'{kind}' is not a security event kind.");
        }

        return new SecurityEvent(
            SecurityEventId.New(),
            occurredAt.ToUniversalTime(),
            kind,
            userId is { IsEmpty: false } ? userId : null,
            tenantId is { IsEmpty: false } ? tenantId : null,
            Key(subjectKey, nameof(subjectKey)),
            Key(clientNetworkKey, nameof(clientNetworkKey)));
    }

    private static string? Key(string? value, string name)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length is > 0 and <= MaxKeyLength
            ? value
            : throw new DomainValidationException($"A security event {name} must be 1 to {MaxKeyLength} characters.");
    }
}

/// <summary>
/// Every kind of security event, as stored.
/// </summary>
/// <remarks>
/// A closed list in code, so a typo is a failed test rather than a kind that no
/// query ever finds.
/// </remarks>
public static class SecurityEventKinds
{
    public const string PasswordResetRequested = "auth.password_reset.requested";
    public const string PasswordResetThrottled = "auth.password_reset.throttled";
    public const string PasswordResetEmailSent = "auth.password_reset.email_sent";
    public const string PasswordResetEmailFailed = "auth.password_reset.email_failed";
    public const string PasswordResetCompleted = "auth.password_reset.completed";
    public const string PasswordResetRejected = "auth.password_reset.rejected";
    public const string PasswordChangedEmailSent = "auth.password_changed.email_sent";
    public const string PasswordChangedEmailFailed = "auth.password_changed.email_failed";
    public const string SessionsRevoked = "auth.sessions.revoked";
    public const string SignInThrottled = "auth.signin.throttled";
    public const string InvitationEmailSent = "auth.invitation.email_sent";
    public const string InvitationEmailFailed = "auth.invitation.email_failed";

    public static IReadOnlyList<string> All { get; } =
    [
        PasswordResetRequested,
        PasswordResetThrottled,
        PasswordResetEmailSent,
        PasswordResetEmailFailed,
        PasswordResetCompleted,
        PasswordResetRejected,
        PasswordChangedEmailSent,
        PasswordChangedEmailFailed,
        SessionsRevoked,
        SignInThrottled,
        InvitationEmailSent,
        InvitationEmailFailed,
    ];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
}
