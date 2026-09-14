using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.WhatsApp;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// Verifies webhook requests against Meta's app secret and verify token.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Both secrets are held as bytes and never exposed.</strong> There is no
/// property that returns either, no <c>ToString</c> that includes them, and
/// nothing here writes to a log. The type answers yes or no and nothing else.
/// </para>
/// <para>
/// <strong>Both comparisons are constant time.</strong> A comparison that returns
/// early on the first differing byte leaks, through timing, how much of a guess
/// was right — and a secret that can be learned one byte at a time is a secret
/// with a few hundred guesses of strength rather than a few hundred bits.
/// </para>
/// </remarks>
internal sealed class MetaWebhookAuthenticator : IWhatsAppWebhookAuthenticator
{
    /// <summary>The prefix Meta puts in front of the hex digest.</summary>
    private const string SignaturePrefix = "sha256=";

    /// <summary>Hex characters in a SHA-256 digest.</summary>
    private const int DigestHexLength = 64;

    private readonly byte[] _appSecret;

    private readonly byte[] _verifyToken;

    /// <summary>Builds an authenticator for this deployment's Meta app.</summary>
    /// <param name="appSecret">
    /// The Meta app secret. Used as the HMAC key exactly as Meta specifies: the
    /// UTF-8 bytes of the secret as displayed in the app dashboard.
    /// </param>
    /// <param name="verifyToken">
    /// The token this deployment expects Meta to echo when subscribing.
    /// </param>
    public MetaWebhookAuthenticator(string appSecret, string verifyToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifyToken);

        _appSecret = Encoding.UTF8.GetBytes(appSecret);
        _verifyToken = Encoding.UTF8.GetBytes(verifyToken);
    }

    public bool IsConfigured => true;

    public bool IsVerifyTokenValid(string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        // FixedTimeEquals over the raw bytes. It returns false for a length
        // mismatch without comparing, which discloses the length of the expected
        // token and nothing about its content -- and the length of a token
        // somebody chose is not the part worth protecting.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            _verifyToken);
    }

    public bool IsSignatureValid(ReadOnlySpan<byte> rawBody, string? header)
    {
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        ReadOnlySpan<char> value = header.AsSpan().Trim();

        if (!value.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> hex = value[SignaturePrefix.Length..];

        if (hex.Length != DigestHexLength)
        {
            return false;
        }

        Span<byte> presented = stackalloc byte[SHA256.HashSizeInBytes];

        if (Convert.FromHexString(hex, presented, out _, out int written) != OperationStatus.Done
            || written != presented.Length)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[SHA256.HashSizeInBytes];

        HMACSHA256.HashData(_appSecret, rawBody, expected);

        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

/// <summary>
/// The authenticator a deployment gets when no app secret was configured.
/// </summary>
/// <remarks>
/// <strong>It refuses everything.</strong> An unconfigured deployment has no way
/// to tell a real webhook from an invented one, and the only safe answer to a
/// question it cannot answer is no. The alternative — accepting unverified
/// payloads until somebody remembers to set the secret — would mean anybody who
/// learned a phone number id could put words into a workspace's inbox.
/// </remarks>
internal sealed class UnconfiguredWebhookAuthenticator(string reason)
    : IWhatsAppWebhookAuthenticator
{
    /// <summary>Why nothing can be verified. For a startup log line, never for a caller.</summary>
    public string Reason { get; } = reason;

    public bool IsConfigured => false;

    public bool IsVerifyTokenValid(string? presented) => false;

    public bool IsSignatureValid(ReadOnlySpan<byte> rawBody, string? header) => false;
}
