using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat;

/// <summary>
/// Minting the credential that lets one anonymous browser keep talking.
/// </summary>
/// <remarks>
/// An Application-owned contract implemented in Infrastructure, for the same
/// reason invitation tokens are: the use cases need a secret and a digest, and
/// how randomness and hashing are obtained is a platform detail. Nothing here
/// names a cryptographic primitive.
/// </remarks>
public interface IWebChatAccessTokens
{
    /// <summary>Creates a fresh secret and the digest to store beside it.</summary>
    NewWebChatAccessToken Create();

    /// <summary>
    /// Turns a secret presented by a browser into the digest to look up.
    /// </summary>
    /// <returns>
    /// The digest, or <see langword="null"/> when the value could not be a token
    /// this platform issued — the wrong length, or characters outside its
    /// alphabet.
    /// </returns>
    /// <remarks>
    /// Rejecting a malformed value before it reaches a query means a junk cookie
    /// costs a string comparison rather than a database round trip, and keeps
    /// anything shaped unlike a token out of the lookup entirely.
    /// </remarks>
    WebChatAccessTokenHash? Recognise(string? token);
}

/// <summary>A newly minted conversation credential.</summary>
/// <param name="Token">
/// The raw secret. It exists in this object and in the cookie built from it, and
/// nowhere else — not in a log, not in a response body, and not in the database.
/// </param>
/// <param name="Hash">The digest to store.</param>
public sealed record NewWebChatAccessToken(string Token, WebChatAccessTokenHash Hash);
