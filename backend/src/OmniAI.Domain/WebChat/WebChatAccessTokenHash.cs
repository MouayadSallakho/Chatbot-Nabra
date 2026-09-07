namespace OmniAI.Domain.WebChat;

/// <summary>
/// The digest of one anonymous visitor's conversation credential.
/// </summary>
/// <remarks>
/// <strong>The raw secret never reaches this model, and never reaches the
/// database.</strong> What is stored is a hash of it, so a copy of the sessions
/// table is not a set of working conversation credentials. A request hashes the
/// secret the browser presented and looks for the session holding that digest,
/// which is the same comparison without ever holding the secret at rest.
/// <para>
/// <strong>A plain digest, not a password KDF</strong> — for the reason set out
/// on the invitation equivalent: the input is 256 bits from a cryptographic RNG,
/// so there is nothing to guess and a deliberately slow hash would only make
/// every message slower. No salt, for the same reason.
/// </para>
/// <para>
/// <strong>Why this is its own type rather than the invitation one.</strong> The
/// two are structurally identical today and mean entirely different things: one
/// admits a named person to a workspace once, the other lets an anonymous browser
/// keep talking in exactly one conversation. Sharing a type would couple two
/// security boundaries whose rules are free to diverge — an invitation digest
/// will plausibly gain an expiry or a rotation the moment invitations do, and a
/// conversation credential should not inherit it. The duplication is a few lines;
/// the coupling would be permanent.
/// </para>
/// </remarks>
public sealed class WebChatAccessTokenHash : IEquatable<WebChatAccessTokenHash>
{
    /// <summary>The length of a SHA-256 digest.</summary>
    public const int Length = 32;

    private readonly byte[] _value;

    private WebChatAccessTokenHash(byte[] value) => _value = value;

    /// <summary>The digest bytes. A copy, so the stored digest cannot be altered.</summary>
    public IReadOnlyList<byte> Value => _value;

    /// <summary>Wraps a digest.</summary>
    /// <exception cref="DomainValidationException">
    /// It is missing or not the length of the digest this platform uses.
    /// </exception>
    public static WebChatAccessTokenHash From(ReadOnlySpan<byte> value)
    {
        if (value.Length != Length)
        {
            throw new DomainValidationException(
                $"A web chat access token hash must be exactly {Length} bytes.");
        }

        return new WebChatAccessTokenHash(value.ToArray());
    }

    /// <summary>The digest as an array, for persistence.</summary>
    public byte[] ToArray() => (byte[])_value.Clone();

    /// <summary>Whether two digests are the same, without leaking where they differ.</summary>
    public bool Equals(WebChatAccessTokenHash? other) =>
        other is not null
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(_value, other._value);

    public override bool Equals(object? obj) => Equals(obj as WebChatAccessTokenHash);

    /// <summary>
    /// Value equality, so <c>==</c> means the same as <see cref="Equals(WebChatAccessTokenHash?)"/>.
    /// </summary>
    /// <remarks>
    /// Declared because the default for a reference type is identity, and an
    /// access check must not depend on which of two spellings the author reached
    /// for.
    /// </remarks>
    public static bool operator ==(WebChatAccessTokenHash? left, WebChatAccessTokenHash? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(WebChatAccessTokenHash? left, WebChatAccessTokenHash? right) =>
        !(left == right);

    /// <remarks>The digest is already uniformly distributed.</remarks>
    public override int GetHashCode() => BitConverter.ToInt32(_value, 0);

    /// <summary>Deliberately says nothing, so no digest reaches a log line.</summary>
    public override string ToString() => "WebChatAccessTokenHash";
}
