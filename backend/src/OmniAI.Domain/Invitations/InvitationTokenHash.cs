namespace OmniAI.Domain.Invitations;

/// <summary>
/// The digest of an invitation's secret.
/// </summary>
/// <remarks>
/// <strong>The raw token never reaches this model, and never reaches the
/// database.</strong> What is stored is a hash of it, so a copy of the
/// invitations table is not a set of working invitation links. Acceptance hashes
/// the token the caller presents and looks for a row with the same digest, which
/// is the same comparison without ever holding the secret at rest.
/// <para>
/// <strong>A plain digest is the right construction here, not a password
/// KDF.</strong> Argon2 or PBKDF2 exist to make guessing survivable when the
/// input is a human-chosen password with perhaps thirty bits of entropy. This
/// input is 256 bits from a cryptographic RNG: there is nothing to guess, and a
/// deliberately slow hash would only make every acceptance slower while adding no
/// security. For the same reason there is no salt -- a salt defeats precomputed
/// tables over a small input space, and this space is not small.
/// </para>
/// <para>
/// <strong>It is bytes, not text.</strong> A digest is binary; rendering it as
/// hex or base64 for storage would make the column a third longer and would
/// invite somebody to display it. Comparison is ordinal over the bytes, in fixed
/// time, because a comparison that returns early on the first differing byte
/// leaks how much of a guess was right.
/// </para>
/// </remarks>
public sealed class InvitationTokenHash : IEquatable<InvitationTokenHash>
{
    /// <summary>The length of a SHA-256 digest.</summary>
    public const int Length = 32;

    private readonly byte[] _value;

    private InvitationTokenHash(byte[] value) => _value = value;

    /// <summary>The digest bytes.</summary>
    /// <remarks>
    /// A copy, so a caller cannot reach in and alter the stored digest.
    /// </remarks>
    public IReadOnlyList<byte> Value => _value;

    /// <summary>Wraps a digest.</summary>
    /// <exception cref="DomainValidationException">
    /// It is missing or not the length of the digest this platform uses.
    /// </exception>
    public static InvitationTokenHash From(ReadOnlySpan<byte> value)
    {
        if (value.Length != Length)
        {
            throw new DomainValidationException(
                $"An invitation token hash must be exactly {Length} bytes.");
        }

        return new InvitationTokenHash(value.ToArray());
    }

    /// <summary>The digest as an array, for persistence.</summary>
    public byte[] ToArray() => (byte[])_value.Clone();

    /// <summary>
    /// Whether two digests are the same, without leaking where they differ.
    /// </summary>
    public bool Equals(InvitationTokenHash? other) =>
        other is not null
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(_value, other._value);

    public override bool Equals(object? obj) => Equals(obj as InvitationTokenHash);

    /// <summary>
    /// Value equality, so <c>==</c> means the same as <see cref="Equals(InvitationTokenHash?)"/>.
    /// </summary>
    /// <remarks>
    /// Declared because the default for a reference type is identity, and a
    /// comparison of two digests that happened to be different objects would then
    /// answer false for two identical tokens. A security check must not depend on
    /// which of two spellings the author reached for.
    /// </remarks>
    public static bool operator ==(InvitationTokenHash? left, InvitationTokenHash? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(InvitationTokenHash? left, InvitationTokenHash? right) =>
        !(left == right);

    /// <remarks>
    /// Derived from the digest, which is already uniformly distributed, so the
    /// first four bytes are as good a hash code as any.
    /// </remarks>
    public override int GetHashCode() => BitConverter.ToInt32(_value, 0);

    /// <summary>
    /// Deliberately says nothing.
    /// </summary>
    /// <remarks>
    /// A digest is not a secret, but printing it into a log or an error message
    /// serves no purpose and makes an offline comparison against a stolen token
    /// list possible. There is no formatting of it anywhere in this platform.
    /// </remarks>
    public override string ToString() => "InvitationTokenHash";
}
