using OmniAI.Application.Concurrency;
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
/// <para>
/// <strong>An expired session is refused the same way.</strong> Not "this
/// conversation exists but your credential has expired", which would confirm the
/// id and tell somebody holding a stale copy that they had once held a real one.
/// One answer covers a conversation that never existed, a credential for a
/// different conversation, a malformed cookie and a session that has run out.
/// </para>
/// </remarks>
public sealed class PublicWebChatAccess(
    IWebChatRuntimeStore store,
    IWebChatEndpointRepository endpoints,
    IWebChatAccessTokens tokens,
    TimeProvider clock)
{
    private readonly IWebChatRuntimeStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IWebChatAccessTokens _tokens =
        tokens ?? throw new ArgumentNullException(nameof(tokens));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

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
    /// malformed, belongs to a different conversation, names a conversation that
    /// does not exist, or opens one whose session has expired.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>The transport is checked before the secret is compared.</strong> A
    /// hosted cookie arriving at an embedded route, or an embedded credential
    /// arriving at a hosted one, is refused outright. Without that the two would
    /// share one authority surface and whichever was easier to attack would become
    /// the strength of both.
    /// </para>
    /// <para>
    /// <strong>An embedded session re-checks its website on every request.</strong>
    /// The allowlist is read now rather than trusted from when the session opened,
    /// so removing a website in the admin stops the widgets already open on it.
    /// </para>
    /// </remarks>
    public async Task<WebChatSession?> AuthoriseAsync(
        ConversationId conversationId,
        string? presentedToken,
        WebChatClientMode mode,
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
        if (session is null || !session.AdmitsMode(mode) || !session.Admits(presented))
        {
            return null;
        }

        // Checked after the digest comparison, not before. Doing it the other way
        // round would answer faster for an expired session than for a wrong
        // credential, which is a difference somebody probing can measure -- and it
        // would mean the platform read a session's expiry before establishing that
        // the caller had any right to it.
        //
        // The stored instant decides, not the current configuration: a deployment
        // that lengthens the session lifetime tomorrow does not thereby revive
        // credentials that expired today.
        if (session.HasExpired(_clock.GetUtcNow()))
        {
            return null;
        }

        // Last, because it costs a second query and only embedded sessions need
        // it. A widget whose website has been removed from the allowlist stops
        // here -- the conversation survives and stays visible in the admin,
        // because the customer really did say those things.
        if (session.IsEmbedded
            && !await IsStillAllowedAsync(session, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return session;
    }

    /// <summary>Whether this embedded session's website is still approved.</summary>
    private async Task<bool> IsStillAllowedAsync(
        WebChatSession session,
        CancellationToken cancellationToken)
    {
        Versioned<WebChatEndpoint>? endpoint = await _endpoints
            .FindAsync(session.TenantId, session.EndpointId, cancellationToken)
            .ConfigureAwait(false);

        return endpoint is not null && endpoint.Value.Allows(session.EmbeddedOrigin);
    }
}
