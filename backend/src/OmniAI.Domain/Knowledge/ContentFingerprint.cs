namespace OmniAI.Domain.Knowledge;

/// <summary>
/// A fixed-size digest identifying one exact piece of content.
/// </summary>
/// <remarks>
/// <para>
/// Compared, never interpreted. Two fingerprints being equal means the text they
/// were taken from is the same text; nothing else is read out of the bytes, and
/// no code path turns one back into content.
/// </para>
/// <para>
/// <strong>Equality is by value, and it has to be.</strong> A record struct over
/// a <c>byte[]</c> would compare references, so a document read back from the
/// database would never match the fingerprint of its own content and every
/// document would be permanently out of date. The comparison here is over the
/// bytes, and it is not constant-time on purpose: this protects nothing, and
/// pretending otherwise would suggest it did.
/// </para>
/// </remarks>
public sealed class ContentFingerprint : IEquatable<ContentFingerprint>
{
    /// <summary>Length of a SHA-256 digest.</summary>
    public const int Length = 32;

    private readonly byte[] _value;

    private ContentFingerprint(byte[] value) => _value = value;

    public static ContentFingerprint From(byte[]? value)
    {
        if (value is null || value.Length != Length)
        {
            throw new DomainValidationException(
                $"A content fingerprint must be exactly {Length} bytes.");
        }

        // Copied, so a caller holding the array cannot mutate a stored
        // fingerprint afterwards and quietly make a stale index look current.
        return new ContentFingerprint([.. value]);
    }

    /// <summary>A copy of the bytes, for persistence.</summary>
    public byte[] ToArray() => [.. _value];

    public bool Equals(ContentFingerprint? other) =>
        other is not null && _value.AsSpan().SequenceEqual(other._value);

    public override bool Equals(object? obj) => Equals(obj as ContentFingerprint);

    public override int GetHashCode() =>
        // The digest is already uniformly distributed, so the first four bytes
        // are as good a hash code as anything derived from all thirty-two.
        BitConverter.ToInt32(_value, 0);

    public static bool operator ==(ContentFingerprint? left, ContentFingerprint? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ContentFingerprint? left, ContentFingerprint? right) =>
        !(left == right);

    public override string ToString() => Convert.ToHexStringLower(_value);
}
