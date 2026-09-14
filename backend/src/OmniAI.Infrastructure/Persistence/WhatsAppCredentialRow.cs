using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// The stored ciphertext of one connection's access token.
/// </summary>
/// <remarks>
/// <strong>A persistence row, deliberately not a Domain type.</strong> The
/// Domain's <see cref="WhatsAppConnection"/> knows only whether a credential
/// exists, never what it is, so that no aggregate, no use case and no API
/// response can hold the token by accident. What remains is a byte array beside
/// a key, which is a storage concern and belongs here.
/// <para>
/// It is mutable and has a parameterless constructor because EF Core materialises
/// it. Nothing outside the WhatsApp repository ever sees an instance.
/// </para>
/// </remarks>
internal sealed class WhatsAppCredentialRow
{
    /// <summary>The connection this credential belongs to.</summary>
    public WhatsAppConnectionId ConnectionId { get; set; }

    /// <summary>
    /// The protected token: a version marker, a nonce, an authentication tag and
    /// the ciphertext, as produced by the secret protector.
    /// </summary>
    /// <remarks>
    /// Bound during encryption to the purpose, the workspace and this connection,
    /// so these bytes moved to another row or another workspace decrypt to
    /// nothing.
    /// </remarks>
    public byte[] ProtectedToken { get; set; } = [];

    /// <summary>When the token was last entered.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
