namespace OmniAI.Application.Invitations;

/// <summary>
/// How long an invitation stays open.
/// </summary>
/// <remarks>
/// <strong>Finite, and one place decides how finite.</strong> An invitation is a
/// bearer secret: whoever holds the link can present it. A link that never
/// expires is one that stays live in a mailbox, a chat archive or a screenshot
/// for as long as those exist, so the window is closed by time as well as by
/// revocation.
/// <para>
/// <strong>A value, not a constant scattered through the code.</strong> The
/// lifetime is supplied at composition, so it is configurable without editing a
/// use case and so tests can issue an invitation that has already expired without
/// waiting a week. Nothing else in the platform is allowed to invent its own
/// expiry: a second opinion about how long an invitation lasts is how one code
/// path accepts what another considers dead.
/// </para>
/// <para>
/// Seven days is the default because it spans a working week -- somebody invited
/// on Friday can still act on Monday -- while being short enough that a forgotten
/// link stops working before anybody has forgotten it exists.
/// </para>
/// </remarks>
public sealed class InvitationPolicy
{
    /// <summary>The lifetime used unless composition says otherwise.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(7);

    /// <summary>Creates a policy.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The lifetime is not positive. An invitation that expires at or before the
    /// moment it is issued could never be used, and would be a configuration
    /// mistake that looked like a platform fault.
    /// </exception>
    public InvitationPolicy(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        Lifetime = lifetime;
    }

    /// <summary>A policy using the default lifetime.</summary>
    public InvitationPolicy()
        : this(DefaultLifetime)
    {
    }

    /// <summary>How long an invitation remains acceptable after it is issued.</summary>
    public TimeSpan Lifetime { get; }

    /// <summary>When an invitation issued now would expire.</summary>
    public DateTimeOffset ExpiresAfter(DateTimeOffset issuedAt) => issuedAt + Lifetime;
}
