using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.Security;

namespace OmniAI.Infrastructure.Security;

/// <summary>
/// Protects secrets with AES-256-GCM under a deployment master key.
/// </summary>
/// <remarks>
/// <strong>Authenticated encryption, not encryption.</strong> AES-CBC or
/// AES-CTR would hide a token perfectly well and leave it malleable: somebody
/// who could write to the database could flip bits in the ciphertext and change
/// the plaintext in predictable ways. GCM produces a tag over both the
/// ciphertext and the associated data, so any change to either — including
/// moving a row's ciphertext to another workspace — makes decryption fail rather
/// than return something.
/// <para>
/// <strong>The associated data is the workspace, the resource and the purpose.</strong>
/// That is what makes a stolen-and-replanted ciphertext useless. Encryption
/// alone protects against a reader; binding the context protects against a
/// writer, which is the more realistic attacker once a database is involved.
/// </para>
/// <para>
/// <strong>A fresh random nonce per call, never a counter.</strong> Reusing a
/// nonce under one key in GCM is catastrophic — it leaks the XOR of two
/// plaintexts and, worse, allows forging the authentication tag. Twelve random
/// bytes is the size the standard specifies, and at this platform's volumes the
/// chance of a repeat is negligible.
/// </para>
/// <para>
/// The stored form is <c>version ‖ nonce ‖ tag ‖ ciphertext</c>. The version
/// byte is what makes a future algorithm change possible without a migration
/// that cannot read what is already there.
/// </para>
/// </remarks>
internal sealed class AesGcmSecretProtector : ISecretProtector
{
    /// <summary>The format this implementation writes.</summary>
    internal const byte Version = 1;

    /// <summary>Nonce size in bytes, as AES-GCM specifies.</summary>
    internal const int NonceBytes = 12;

    /// <summary>Authentication tag size in bytes: the full 128 bits.</summary>
    internal const int TagBytes = 16;

    /// <summary>Master key size in bytes. AES-256, so nothing shorter is accepted.</summary>
    internal const int KeyBytes = 32;

    private readonly byte[] _key;

    internal AesGcmSecretProtector(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length != KeyBytes)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The secret master key must be exactly {KeyBytes} bytes."),
                nameof(key));
        }

        _key = [.. key];
    }

    public byte[] Protect(string secret, SecretContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] associatedData = Bind(context);

        byte[] output = new byte[1 + NonceBytes + TagBytes + plaintext.Length];

        output[0] = Version;

        Span<byte> nonce = output.AsSpan(1, NonceBytes);
        Span<byte> tag = output.AsSpan(1 + NonceBytes, TagBytes);
        Span<byte> ciphertext = output.AsSpan(1 + NonceBytes + TagBytes);

        RandomNumberGenerator.Fill(nonce);

        using AesGcm aes = new(_key, TagBytes);

        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        // The plaintext copy this method made is wiped. It does not make the
        // secret unrecoverable -- the caller still holds the string, and .NET
        // strings are immutable and not pinned -- but it removes one avoidable
        // copy from a heap that may end up in a dump.
        CryptographicOperations.ZeroMemory(plaintext);

        return output;
    }

    public string? Unprotect(byte[]? protectedSecret, SecretContext context)
    {
        if (protectedSecret is null
            || protectedSecret.Length <= 1 + NonceBytes + TagBytes
            || protectedSecret[0] != Version)
        {
            return null;
        }

        ReadOnlySpan<byte> stored = protectedSecret;
        ReadOnlySpan<byte> nonce = stored.Slice(1, NonceBytes);
        ReadOnlySpan<byte> tag = stored.Slice(1 + NonceBytes, TagBytes);
        ReadOnlySpan<byte> ciphertext = stored[(1 + NonceBytes + TagBytes)..];

        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using AesGcm aes = new(_key, TagBytes);

            aes.Decrypt(nonce, ciphertext, tag, plaintext, Bind(context));

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            // Tampered, foreign, or protected under another key. One answer for
            // all three: telling them apart would tell an attacker which of
            // their guesses was closest.
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Builds the associated data a ciphertext is bound to.
    /// </summary>
    /// <remarks>
    /// Length-prefixed rather than concatenated. Without the prefix, a purpose
    /// of <c>"ab"</c> with one identifier and <c>"a"</c> with another could
    /// produce the same bytes, and two different contexts that serialise
    /// identically are two contexts the tag cannot tell apart.
    /// </remarks>
    private static byte[] Bind(SecretContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Purpose);

        byte[] purpose = Encoding.UTF8.GetBytes(context.Purpose);
        byte[] bound = new byte[4 + purpose.Length + 16 + 16];

        BinaryPrimitives.WriteInt32BigEndian(bound, purpose.Length);

        purpose.CopyTo(bound, 4);

        context.TenantId.TryWriteBytes(bound.AsSpan(4 + purpose.Length, 16));
        context.ResourceId.TryWriteBytes(bound.AsSpan(4 + purpose.Length + 16, 16));

        return bound;
    }
}

/// <summary>
/// The protector on a deployment that has no master key configured.
/// </summary>
/// <remarks>
/// <strong>It refuses rather than storing anything.</strong> The alternative —
/// falling back to plaintext, or to a per-process key — would mean a
/// misconfigured deployment silently storing customer credentials in the clear,
/// or storing them under a key that vanishes on restart and leaves a workspace
/// with a channel it cannot use and cannot fix.
/// <para>
/// Unprotecting answers null, which is what every caller already handles: a
/// connection whose token cannot be recovered simply does not send.
/// </para>
/// </remarks>
internal sealed class UnconfiguredSecretProtector(string reason) : ISecretProtector
{
    private readonly string _reason = reason;

    public byte[] Protect(string secret, SecretContext context) =>
        throw new SecretProtectionUnavailableException(_reason);

    public string? Unprotect(byte[]? protectedSecret, SecretContext context) => null;
}
