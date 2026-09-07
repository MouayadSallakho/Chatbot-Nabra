using System.Net;

namespace OmniAI.Application.WebChat.Protection;

/// <summary>
/// A pseudonymous stand-in for one visitor's network location.
/// </summary>
/// <remarks>
/// <strong>This type exists so that no address does.</strong> Rate limiting needs
/// to tell one network apart from another; it does not need to know, record or be
/// able to reconstruct where anybody is. An IP address is personal data in most of
/// the jurisdictions this platform will be sold into, and a counter store is
/// exactly the sort of place it ends up living forever by accident — in a key, in
/// a log line beside a key, in a support screenshot of that log.
/// <para>
/// So the address is turned into a keyed digest before it crosses into the
/// Application, and this is the only form any of the protection code ever sees.
/// The value is stable — the same network yields the same key, which is what makes
/// counting possible — and one-way, so possession of the whole key space plus the
/// counter store still does not yield an address without the server's secret.
/// </para>
/// <para>
/// <strong>It is not a credential and authorises nothing.</strong> It identifies a
/// bucket to count in. Nothing in the platform grants access on the strength of a
/// network key, and nothing publishes one: it is never returned in a response,
/// never put in a cookie, and never shown to a visitor.
/// </para>
/// <para>
/// A <c>readonly record struct</c> for the same reason every identifier here is
/// one: it costs no allocation, compares by value, and cannot be confused with a
/// bare <see cref="string"/> at a call site — which for a value that must never
/// be swapped with a raw address is the point.
/// </para>
/// </remarks>
public readonly record struct ClientNetworkKey
{
    private ClientNetworkKey(string value) => Value = value;

    /// <summary>The opaque key text. Never an address, and never reversible.</summary>
    public string Value { get; }

    /// <summary>Whether this is the default, uninitialised value.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>
    /// Wraps an already-computed pseudonymous key.
    /// </summary>
    /// <remarks>
    /// Takes the finished digest rather than an <see cref="IPAddress"/>, because
    /// computing it needs a secret and a keyed hash and both belong to
    /// Infrastructure. See <see cref="IClientNetworkKeys"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">The key is blank.</exception>
    public static ClientNetworkKey From(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A client network key cannot be blank.", nameof(value))
            : new ClientNetworkKey(value);

    /// <summary>
    /// The key text, so it can be used as part of a counter name.
    /// </summary>
    /// <remarks>
    /// Safe to render, unlike every other secret-shaped value in this platform,
    /// because it is not a secret — it is the whole point of the type that it can
    /// be written down without writing down an address. It is still not logged
    /// beside anything that would re-identify it.
    /// </remarks>
    public override string ToString() => Value;
}

/// <summary>
/// Turning a client address into something safe to count with.
/// </summary>
/// <remarks>
/// An Application-owned contract implemented in Infrastructure, for the same
/// reason invitation tokens and chat credentials are: the use cases need a stable
/// pseudonym, and how it is derived — which hash, which secret, how an address is
/// normalised first — is a platform detail. Nothing in this layer names a
/// cryptographic primitive.
/// </remarks>
public interface IClientNetworkKeys
{
    /// <summary>
    /// The pseudonymous key for one client address.
    /// </summary>
    /// <param name="address">
    /// The address the connection genuinely came from, after the trusted-proxy
    /// rules have been applied. Passing an address taken from an unverified
    /// forwarded header would make every limit here bypassable by anybody willing
    /// to set a header, so the caller is responsible for having established it.
    /// </param>
    /// <exception cref="PublicChatProtectionUnavailableException">
    /// This deployment has no key secret configured, so no trustworthy key can be
    /// derived. Thrown rather than substituted, because a shared fallback bucket
    /// would be one unmetered lane through every limit.
    /// </exception>
    ClientNetworkKey Derive(IPAddress address);
}
