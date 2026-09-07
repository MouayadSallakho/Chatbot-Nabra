using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Hosted;

/// <summary>
/// Deciding whether an anonymous browser may act in a conversation.
/// </summary>
/// <remarks>
/// <strong>This is the whole public authorisation model, and it is deliberately
/// one small thing.</strong> There is no user, no membership, no permission and
/// no workspace in it. A caller presents a secret; either it is the secret that
/// opened exactly this conversation, or the caller is treated as though the
/// conversation does not exist.
/// <para>
/// <strong>It can never satisfy admin authorisation.</strong> It returns a
/// <see cref="WebChatSession"/> and nothing else — no principal, no claim, no
/// tenant decision. <c>TenantAuthorizer</c> does not know this type exists and
/// takes no parameter it could be passed as, so there is no expression anywhere
/// in the platform in which holding a conversation credential contributes to an
/// admin decision.
/// </para>
/// <para>
/// <strong>Not-found, never forbidden.</strong> Every refusal here is the same
/// answer an unknown conversation gets. A distinct "wrong credential" would
/// confirm that a conversation id is real, which is exactly the fact somebody
/// walking ids is trying to learn.
/// </para>
/// </remarks>
public sealed class PublicWebChatAccess(
    IWebChatRuntimeStore store,
    IWebChatAccessTokens tokens)
{
    private readonly IWebChatRuntimeStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IWebChatAccessTokens _tokens =
        tokens ?? throw new ArgumentNullException(nameof(tokens));

    /// <summary>
    /// The session a presented secret opens on this conversation, if it does.
    /// </summary>
    /// <param name="conversationId">The conversation the caller named.</param>
    /// <param name="presentedToken">
    /// The raw secret from the visitor's cookie. Never logged, never echoed, and
    /// never stored — it is turned into a digest and compared.
    /// </param>
    /// <returns>
    /// The session, or <see langword="null"/> when the secret is missing,
    /// malformed, belongs to a different conversation, or names a conversation
    /// that does not exist.
    /// </returns>
    public async Task<WebChatSession?> AuthoriseAsync(
        ConversationId conversationId,
        string? presentedToken,
        CancellationToken cancellationToken = default)
    {
        // Shape first, so a junk cookie costs a string comparison rather than a
        // database round trip -- and so nothing shaped unlike a token ever
        // reaches a query.
        WebChatAccessTokenHash? presented = _tokens.Recognise(presentedToken);

        if (presented is null)
        {
            return null;
        }

        WebChatSession? session = await _store
            .FindSessionAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);

        // The session is looked up by the conversation the caller named, and the
        // secret is then compared against that session's own digest. This is what
        // makes a credential useless anywhere but where it was issued: presenting
        // conversation A's secret against conversation B loads B's session, whose
        // digest does not match, and the answer is not-found.
        if (session is null || !session.Admits(presented))
        {
            return null;
        }

        return session;
    }
}
