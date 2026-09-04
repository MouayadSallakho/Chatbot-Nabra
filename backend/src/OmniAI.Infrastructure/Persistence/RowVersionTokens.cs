using System.Buffers.Binary;
using System.Buffers.Text;
using OmniAI.Application.Concurrency;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Translates between PostgreSQL's row version and the opaque token the rest of
/// the platform passes around.
/// </summary>
/// <remarks>
/// <para>
/// This class is the entire boundary. Above it, a version is a
/// <see cref="ConcurrencyToken"/> that nobody reads; below it, it is the
/// <c>xmin</c> system column that PostgreSQL stamps on every row it writes. The
/// Application never sees the number, and the Domain never learns that versions
/// exist at all.
/// </para>
/// <para>
/// <strong>Encoding: the four bytes of the transaction id, big-endian,
/// base64url.</strong> Chosen because it round-trips exactly, is stable across
/// processes and restarts, is short enough for a header, and is URL- and
/// header-safe without escaping. Big-endian so the bytes do not depend on the
/// machine that produced them.
/// </para>
/// <para>
/// <strong>It is opaque, not secret.</strong> The value is handed out as an HTTP
/// <c>ETag</c> and is not sensitive: a transaction id reveals nothing about
/// anyone's data. Opacity buys two things that matter — clients cannot build
/// logic on the internals and then break when this changes, and the header does
/// not advertise the storage engine. Deliberately no HMAC and no encryption:
/// signing a value that is not a secret would add key management, rotation and a
/// failure mode in exchange for an assurance nothing needs. Tampering is not a
/// threat here, because a forged token cannot do anything a real one could not --
/// the worst outcome is a write refused, or a write that the row's actual
/// version happens to permit anyway.
/// </para>
/// <para>
/// <strong>A token that will not decode is treated as "not current", not as a
/// broken request.</strong> Anything that is not exactly four base64url bytes --
/// a value from an older encoding, a truncated header, something invented -- is
/// certainly not the version of this row, so refusing the write is both correct
/// and safe. The caller is told to re-read, which is the action that actually
/// recovers; calling it a malformed request would say the client is broken when
/// the honest answer is that it is out of date.
/// </para>
/// </remarks>
internal static class RowVersionTokens
{
    private const int RowVersionBytes = sizeof(uint);

    /// <summary>Encodes a stored row version as an opaque token.</summary>
    public static ConcurrencyToken Encode(uint rowVersion)
    {
        Span<byte> buffer = stackalloc byte[RowVersionBytes];

        BinaryPrimitives.WriteUInt32BigEndian(buffer, rowVersion);

        return ConcurrencyToken.From(Base64Url.EncodeToString(buffer));
    }

    /// <summary>
    /// Decodes an opaque token back to the row version it represents.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> and the row version, or <see langword="false"/> for
    /// any value this platform did not produce.
    /// </returns>
    public static bool TryDecode(ConcurrencyToken token, out uint rowVersion)
    {
        rowVersion = 0;

        if (token.IsEmpty)
        {
            return false;
        }

        ReadOnlySpan<char> encoded = token.Value;

        // Checked before decoding, because despite its name
        // Base64Url.TryDecodeFromChars throws FormatException on a non-base64url
        // character rather than returning false. This value arrives straight from
        // a client's If-Match header, so letting that escape would turn "your
        // header contains rubbish" into an unhandled exception and a 500.
        if (!Base64Url.IsValid(encoded))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[RowVersionBytes];

        try
        {
            // Belt and braces. IsValid should have settled it, but the cost of
            // these two disagreeing is an unhandled exception on a request path a
            // client controls, and the cost of catching is nothing on the path
            // that matters.
            if (!Base64Url.TryDecodeFromChars(encoded, buffer, out int written)
                || written != RowVersionBytes)
            {
                return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        rowVersion = BinaryPrimitives.ReadUInt32BigEndian(buffer);

        return true;
    }
}
