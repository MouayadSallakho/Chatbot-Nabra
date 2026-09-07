using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.WebChat;
using OmniAI.Domain.WebChat;

namespace OmniAI.Infrastructure.WebChat;

/// <summary>
/// Minting the opaque key that names a hosted chat page.
/// </summary>
/// <remarks>
/// <strong>Random, but not a secret.</strong> This value is published in every
/// hosted chat URL. Randomness is here to stop somebody walking the platform's
/// chat endpoints one integer at a time — harvesting agent names and spending
/// every workspace's provider budget — not to keep the value private. Nothing is
/// hashed, and nothing treats holding it as evidence of anything.
/// <para>
/// 128 bits is ample for that job: enumeration is hopeless long before it is
/// worth attempting, and a shorter URL is a real product benefit for something
/// people paste into websites and emails.
/// </para>
/// </remarks>
internal sealed class WebChatPublicKeys : IWebChatPublicKeys
{
    private const int KeyBytes = 16;

    public WebChatPublicKey Create()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(KeyBytes);

        Span<char> encoded = stackalloc char[WebChatPublicKey.Length + 4];

        if (!Base64Url.TryEncodeToChars(raw, encoded, out int written))
        {
            // Unreachable for a fixed 16-byte input, and thrown rather than
            // papered over: a key that failed to encode must never become a
            // shorter one.
            throw new InvalidOperationException("A web chat public key could not be encoded.");
        }

        return WebChatPublicKey.Create(new string(encoded[..written]));
    }
}

/// <summary>
/// Minting and recognising the credential that continues one conversation.
/// </summary>
/// <remarks>
/// <strong>This is the file the privacy of every public conversation rests
/// on.</strong> Whoever holds one of these values can read and continue exactly
/// one anonymous conversation, so the value has to be genuinely unguessable and
/// must not be recoverable from anything this platform stores.
/// <para>
/// <strong>256 bits from the operating system's CSPRNG</strong> —
/// <see cref="RandomNumberGenerator"/>, not <c>Random</c>, not a <c>Guid</c>. A
/// UUIDv7 embeds the millisecond it was created and leaves far fewer
/// unpredictable bits, which for a value that can be attacked offline is a real
/// difference rather than a theoretical one.
/// </para>
/// <para>
/// <strong>Only the digest is ever returned for storage.</strong> The raw value
/// is handed back once, to the request that started the conversation, and this
/// class keeps no copy. There is no cache, no static, no log statement and no
/// diagnostic here that could hold one.
/// </para>
/// </remarks>
internal sealed class WebChatAccessTokens : IWebChatAccessTokens
{
    /// <summary>How much randomness a credential carries.</summary>
    private const int TokenBytes = 32;

    /// <summary>
    /// The exact length of the encoded form: 32 bytes is 43 base64 characters
    /// without padding.
    /// </summary>
    private const int EncodedLength = 43;

    public NewWebChatAccessToken Create()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(TokenBytes);

        try
        {
            string token = Encode(raw);

            return new NewWebChatAccessToken(token, Digest(token));
        }
        finally
        {
            // The bytes are of no further use, and leaving them in a reused
            // buffer is a small, free risk not to take.
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public WebChatAccessTokenHash? Recognise(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != EncodedLength)
        {
            return null;
        }

        foreach (char character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return null;
            }
        }

        return Digest(token);
    }

    /// <summary>
    /// The digest of a credential's exact encoded text.
    /// </summary>
    /// <remarks>
    /// Hashed as the ASCII the caller presented rather than as decoded bytes, so
    /// there is exactly one representation to reason about and no question of two
    /// encodings of the same value producing different digests.
    /// </remarks>
    private static WebChatAccessTokenHash Digest(string token)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(Encoding.ASCII.GetBytes(token), hash);

        return WebChatAccessTokenHash.From(hash);
    }

    private static string Encode(ReadOnlySpan<byte> value)
    {
        Span<char> encoded = stackalloc char[EncodedLength + 4];

        if (Base64Url.TryEncodeToChars(value, encoded, out int written))
        {
            return new string(encoded[..written]);
        }

        throw new InvalidOperationException("A web chat access token could not be encoded.");
    }
}
