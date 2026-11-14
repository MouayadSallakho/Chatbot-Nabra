using System.Globalization;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Accounts;
using OmniAI.Domain.Security;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Accounts;

/// <summary>
/// Each person's stored language (Phase 37.3, M1).
/// </summary>
/// <remarks>
/// <strong>Set once by the first entry point, changed only on purpose.</strong>
/// Sign-up, sign-in and invitation acceptance fill it only while it is empty
/// (<see cref="FillIfUnsetAsync"/>); only the person's own choice replaces a value
/// (<see cref="SetAsync"/>).
/// </remarks>
public interface IAccountLanguages
{
    /// <returns>False when the account no longer exists.</returns>
    Task<bool> SetAsync(UserId userId, UserLanguage language, CancellationToken cancellationToken = default);

    Task FillIfUnsetAsync(UserId userId, UserLanguage language, CancellationToken cancellationToken = default);
}

/// <summary>
/// Each person's display name (Phase 39.1, note 7).
/// </summary>
/// <remarks>
/// <strong>Only the person's own.</strong> The one caller passes the signed-in
/// account's id; there is no route that names another account, so nobody can edit
/// a colleague's name. Last write wins, like the language: it is one person's own
/// value, with no workflow behind it.
/// </remarks>
public interface IAccountDisplayNames
{
    /// <param name="displayName">The new name, or <see langword="null"/> to clear it.</param>
    /// <returns>False when the account no longer exists.</returns>
    Task<bool> SetAsync(UserId userId, MemberDisplayName? displayName, CancellationToken cancellationToken = default);
}

/// <summary>Writes audit records. Never throws for a record that could not be written.</summary>
/// <remarks>
/// <strong>Best effort, and said so.</strong> An audit write that fails is logged
/// and the request carries on: refusing a sign-in because the audit table is
/// unreachable would turn an audit outage into an outage of everything. The
/// security decisions themselves (limits, tokens, stamps) never depend on it.
/// </remarks>
public interface ISecurityEventLog
{
    Task RecordAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// A keyed hash of a normalised email address, for counting and auditing requests
/// that name it without holding it.
/// </summary>
public interface IAccountSubjectKeys
{
    /// <param name="normalizedEmail">The address as Identity normalises it.</param>
    string Derive(string normalizedEmail);
}

/// <summary>What the account limiter decided.</summary>
public sealed record AccountProtectionDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static readonly AccountProtectionDecision Allow = new(true, TimeSpan.Zero);

    public static AccountProtectionDecision Reject(TimeSpan retryAfter) =>
        new(false, retryAfter < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : retryAfter);
}

/// <summary>
/// The account limiter's store could not be reached, so the request was refused
/// rather than served without a limit.
/// </summary>
public sealed class AccountProtectionUnavailableException : Exception
{
    public AccountProtectionUnavailableException()
    {
    }

    public AccountProtectionUnavailableException(string message)
        : base(message)
    {
    }

    public AccountProtectionUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Rate limits on the anonymous account endpoints, shared by every API instance.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Counted by address and by network, known or not.</strong> The address
/// counter is keyed by <see cref="IAccountSubjectKeys"/> and counts requests for
/// addresses with no account exactly like the others, so a limit never says that an
/// account exists.
/// </para>
/// <para>
/// <strong>Sign-in counts failures, not attempts</strong> (Phase 37.3 Revision 1,
/// D-2: no hard lockout, and nobody can lock another person out).
/// <see cref="CheckSignInAsync"/> reads the counters before the password is checked
/// and, when there is room, reserves an in-flight slot atomically, so parallel guesses
/// cannot all slip past before the first is counted; <see cref="RecordSignInFailureAsync"/>
/// turns the slot into a counted failure (an unknown address is a failure too); a
/// successful sign-in counts nothing: <see cref="RecordSignInSuccessAsync"/> releases
/// its slot and clears that address's count on that network. So a team behind one address is never refused for signing in, and
/// guesses from one network slow only that network.
/// </para>
/// <para>
/// <strong>Fails closed.</strong> When the store cannot be reached the calls throw
/// <see cref="AccountProtectionUnavailableException"/>, and the endpoint refuses
/// with 503. So does a request whose client network cannot be established:
/// unknown is not a bucket (public-chat-protection.md P11).
/// </para>
/// </remarks>
public interface IAccountProtection
{
    /// <summary>
    /// Reads the three sign-in failure counters and, when none is full, reserves an
    /// in-flight slot in each. Every allowed check must be followed by
    /// <see cref="RecordSignInFailureAsync"/> or <see cref="RecordSignInSuccessAsync"/>;
    /// a slot left behind expires within seconds.
    /// </summary>
    Task<AccountProtectionDecision> CheckSignInAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default);

    /// <summary>Counts one failed sign-in in all three counters, releasing its slot.</summary>
    Task RecordSignInFailureAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default);

    /// <summary>Releases the sign-in's slot and clears the failures of this address on this network. Best effort.</summary>
    Task RecordSignInSuccessAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default);

    Task<AccountProtectionDecision> AuthorisePasswordResetRequestAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default);

    Task<AccountProtectionDecision> AuthorisePasswordResetAsync(
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears an address's sign-in failures after a completed password reset
    /// (Phase 37.5b, the owner's decision after 37.3).
    /// </summary>
    /// <remarks>
    /// Two counters, both the address's own: its ceiling across every network, and
    /// its count on the network the reset came from. The person has just proved
    /// they hold the inbox, so they can sign in at once. The per-network counter is
    /// left alone: it counts every address on that network, and a reset must never
    /// clear another address's failures. Best effort, like a successful sign-in's
    /// clear.
    /// </remarks>
    Task ClearSignInFailuresAsync(
        string subjectKey,
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts an anonymous session check from a network (Phase 37.5b, row 29).
    /// </summary>
    /// <remarks>
    /// Every caller counts, signed in or not, so the check cannot be used to probe
    /// sessions faster than the limit. Fails closed like the other limits.
    /// </remarks>
    Task<AccountProtectionDecision> AuthoriseSessionCheckAsync(
        ClientNetworkKey? client,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The account limits. Deployment configuration, section <c>AccountProtection</c>.
/// </summary>
public sealed class AccountProtectionOptions
{
    public const string SectionName = "AccountProtection";

    /// <summary>
    /// Failed sign-ins for one address from one network (Revision 1): stops guessing,
    /// and slows only the network the guesses come from.
    /// </summary>
    public RateLimitWindow SignInFailuresPerAddressOnNetwork { get; init; } = new(5, TimeSpan.FromMinutes(15));

    /// <summary>Failed sign-ins from one network, whatever the address.</summary>
    public RateLimitWindow SignInFailuresPerNetwork { get; init; } = new(50, TimeSpan.FromMinutes(15));

    /// <summary>Failed sign-ins for one address from every network: a ceiling against spread-out guessing.</summary>
    public RateLimitWindow SignInFailuresPerAddress { get; init; } = new(100, TimeSpan.FromHours(1));

    /// <summary>Reset requests per address: three emails an hour at most.</summary>
    public RateLimitWindow ResetRequestsPerAddress { get; init; } = new(3, TimeSpan.FromHours(1));

    /// <summary>Reset requests per network, whatever the address.</summary>
    public RateLimitWindow ResetRequestsPerNetwork { get; init; } = new(10, TimeSpan.FromHours(1));

    /// <summary>Attempts to set a new password with a link, per network.</summary>
    public RateLimitWindow ResetsPerNetwork { get; init; } = new(20, TimeSpan.FromMinutes(15));

    /// <summary>
    /// Anonymous session checks per network (Phase 37.5b). Generous, because an
    /// office of people behind one address each open the app several times a day:
    /// one a second on average over ten minutes.
    /// </summary>
    public RateLimitWindow SessionChecksPerNetwork { get; init; } = new(600, TimeSpan.FromMinutes(10));

    /// <summary>Namespaces the counters, like the public chat protection's prefix.</summary>
    public string KeyPrefix { get; init; } = "omniai:acp:v1";

    public void Validate()
    {
        SignInFailuresPerAddressOnNetwork.Validate($"{SectionName}:{nameof(SignInFailuresPerAddressOnNetwork)}");
        SignInFailuresPerNetwork.Validate($"{SectionName}:{nameof(SignInFailuresPerNetwork)}");
        SignInFailuresPerAddress.Validate($"{SectionName}:{nameof(SignInFailuresPerAddress)}");
        ResetRequestsPerAddress.Validate($"{SectionName}:{nameof(ResetRequestsPerAddress)}");
        ResetRequestsPerNetwork.Validate($"{SectionName}:{nameof(ResetRequestsPerNetwork)}");
        ResetsPerNetwork.Validate($"{SectionName}:{nameof(ResetsPerNetwork)}");
        SessionChecksPerNetwork.Validate($"{SectionName}:{nameof(SessionChecksPerNetwork)}");

        if (string.IsNullOrWhiteSpace(KeyPrefix))
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"{SectionName}:{nameof(KeyPrefix)} cannot be blank."));
        }
    }
}

/// <summary>
/// The account emails' queue, and the one statement a reset request runs.
/// </summary>
public interface IAccountEmailOutbox
{
    /// <summary>
    /// Records a reset request: one lookup, a queued email when the address has an
    /// account and none is already pending, and the audit row, whether or not the
    /// account exists.
    /// </summary>
    /// <remarks>
    /// <strong>The same statements either way.</strong> The address is looked up,
    /// and the email queued or not, inside the same two statements, so the work done
    /// for an unknown address is the work done for a known one. Nothing about the
    /// outcome is returned.
    /// </remarks>
    Task RequestPasswordResetAsync(
        string normalizedEmail,
        string subjectKey,
        ClientNetworkKey? client,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task QueueAsync(AccountEmail email, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ends every open realtime connection of one account, on every API instance.
/// </summary>
/// <remarks>
/// HTTP requests are refused by the per-request stamp check as soon as the new
/// stamp is stored. A SignalR connection is authenticated once, when it opens, so
/// it is closed explicitly: on this instance at once, and on the others through
/// the shared Redis.
/// </remarks>
public interface ISessionRevocation
{
    Task RevokeAsync(UserId userId, CancellationToken cancellationToken = default);
}
