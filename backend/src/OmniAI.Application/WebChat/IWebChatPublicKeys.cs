using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat;

/// <summary>
/// Minting the opaque key that names a hosted chat page.
/// </summary>
/// <remarks>
/// Separate from <see cref="IWebChatAccessTokens"/> because the two produce
/// values with opposite rules: this one is published and stored in the clear,
/// that one is a secret and is stored only as a digest. Giving them one interface
/// would have made it possible to reach for the wrong method and be wrong in the
/// dangerous direction without anything looking odd at the call site.
/// </remarks>
public interface IWebChatPublicKeys
{
    /// <summary>A fresh, unguessable public key.</summary>
    WebChatPublicKey Create();
}
