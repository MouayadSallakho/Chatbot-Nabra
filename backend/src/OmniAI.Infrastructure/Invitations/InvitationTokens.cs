using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.Invitations;
using OmniAI.Domain.Invitations;

namespace OmniAI.Infrastructure.Invitations;

/// <summary>
/// Minting and recognising invitation secrets.
/// </summary>
/// <remarks>
/// <strong>The security of the whole invitation flow rests on this file.</strong>
/// An invitation link is a bearer credential: whoever holds it can present it,
/// and the only other thing standing between a holder and a workspace is the
/// requirement to be signed in as the invited address. So the token has to be
/// genuinely unguessable, and it has to be unguessable in a way that does not
/// depend on anybody remembering to keep it secret at rest.
/// <para>
/// <strong>256 bits from the operating system's CSPRNG.</strong>
/// <see cref="RandomNumberGenerator"/>, not <c>Random</c>, not a
/// <c>Guid</c> and not a timestamp. A GUID -- including a UUIDv7 -- is an
/// identifier, not a secret: v7 embeds the millisecond it was created and leaves
/// far fewer unpredictable bits, so a few thousand guesses around a known
/// invitation time would be a realistic attack. Thirty-two random bytes make the
/// search space large enough that guessing is not a strategy anybody would
/// attempt.
/// </para>
/// <para>
/// <strong>Base64url without padding, so the token survives a URL
/// fragment.</strong> The alphabet avoids <c>+</c>, <c>/</c> and <c>=</c>, none
/// of which travel through a fragment, a copy-paste or a chat client
/// unmolested -- and a token that arrives subtly altered would look to its holder
/// like an invitation that had been withdrawn.
/// </para>
/// <para>
/// <strong>Only the digest is ever returned for storage.</strong> The raw value
/// is handed back once, to the request that created it, and this class keeps no
/// copy. There is no cache, no static, no log statement and no diagnostic here
/// that could hold one.
/// </para>
/// </remarks>
internal sealed class InvitationTokens : IInvitationTokens
{
    /// <summary>How much randomness a token carries.</summary>
    /// <remarks>
    /// Thirty-two bytes -- 256 bits. Chosen to match the digest that stores it, so
    /// neither half is the weaker one.
    /// </remarks>
    private const int TokenBytes = 32;

    /// <summary>The exact length of the encoded form.</summary>
    /// <remarks>
    /// 32 bytes is 43 base64 characters without padding. Fixed, so a value of any
    /// other length is rejected before it can reach the database.
    /// </remarks>
    private const int EncodedLength = 43;

    public NewInvitationToken Create()
    {
        byte[] raw = RandomNumberGenerator.GetBytes(TokenBytes);

        try
        {
            string token = Encode(raw);

            return new NewInvitationToken(token, Digest(token));
        }
        finally
        {
            // The bytes are of no further use, and leaving them in a pooled or
            // reused buffer is a small, free risk not to take.
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public InvitationTokenHash? Recognise(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != EncodedLength)
        {
            return null;
        }

        foreach (char character in token)
        {
            if (!IsBase64UrlCharacter(character))
            {
                return null;
            }
        }

        return Digest(token);
    }

    /// <summary>
    /// The digest of a token's exact encoded text.
    /// </summary>
    /// <remarks>
    /// Hashed as the ASCII the caller presented rather than as decoded bytes, so
    /// there is exactly one representation to reason about and no question of two
    /// encodings of the same value producing different digests.
    /// </remarks>
    private static InvitationTokenHash Digest(string token)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];

        SHA256.HashData(Encoding.ASCII.GetBytes(token), hash);

        return InvitationTokenHash.From(hash);
    }

    private static string Encode(ReadOnlySpan<byte> value)
    {
        Span<char> encoded = stackalloc char[EncodedLength + 4];

        if (Base64Url.TryEncodeToChars(value, encoded, out int written))
        {
            return new string(encoded[..written]);
        }

        // Unreachable for a fixed 32-byte input, and thrown rather than papered
        // over: a token that failed to encode must never become a shorter one.
        throw new InvalidOperationException("An invitation token could not be encoded.");
    }

    private static bool IsBase64UrlCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
