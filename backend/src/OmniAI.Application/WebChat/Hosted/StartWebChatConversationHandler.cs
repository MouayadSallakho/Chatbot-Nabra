using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Hosted;

/// <summary>Read the public face of one hosted chat endpoint.</summary>
/// <param name="PublicKey">The key from the page's address.</param>
public sealed record GetPublicWebChatQuery(string? PublicKey);

/// <summary>Start a conversation on one hosted chat endpoint.</summary>
/// <remarks>
/// <strong>It carries a key and nothing else.</strong> There is no tenant, no
/// agent and no channel in this command, because a visitor must not be able to
/// choose any of them. The server reads all three from the endpoint the key
/// names, which is the only reason a public URL cannot be edited into a request
/// against somebody else's workspace.
/// </remarks>
public sealed record StartWebChatConversationCommand(string? PublicKey);

/// <summary>
/// The public face of a hosted chat endpoint, and starting a conversation on it.
/// </summary>
/// <remarks>
/// Both operations are here because they are the same lookup with the same
/// refusals, and separating them would have meant writing the "is this endpoint
/// serving anybody" rule twice.
/// </remarks>
public sealed class StartWebChatConversationHandler(
    IWebChatEndpointRepository endpoints,
    IWebChatRuntimeStore store,
    IWebChatAccessTokens tokens,
    TimeProvider clock)
{
    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IWebChatRuntimeStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IWebChatAccessTokens _tokens =
        tokens ?? throw new ArgumentNullException(nameof(tokens));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// What the hosted page should render, or <see langword="null"/> for an
    /// unknown key.
    /// </summary>
    /// <remarks>
    /// <strong>Unknown and unavailable are answered differently, and that is
    /// deliberate.</strong> A key that names nothing is not-found, because there
    /// is nothing to render. A key that names a real endpoint which is not
    /// serving returns a page saying the chat is unavailable — the visitor
    /// followed a real link and deserves to be told the chat is off rather than
    /// that their link is broken. What the answer never distinguishes is *why* it
    /// is off: draft, disabled, and a disabled agent are one state to the outside.
    /// </remarks>
    public async Task<PublicWebChatView?> GetAsync(
        GetPublicWebChatQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        PublicWebChatEndpoint? endpoint = await ResolveAsync(query.PublicKey, cancellationToken)
            .ConfigureAwait(false);

        if (endpoint is null)
        {
            return null;
        }

        return new PublicWebChatView(
            endpoint.AiAgentName,
            IsServing(endpoint));
    }

    /// <summary>
    /// Starts a conversation and issues the credential that will continue it.
    /// </summary>
    /// <returns>
    /// The started conversation and the raw secret to put in the visitor's
    /// cookie, or <see langword="null"/> for an unknown key.
    /// </returns>
    /// <exception cref="WebChatUnavailableException">
    /// The endpoint or its agent is not serving the public.
    /// </exception>
    public async Task<StartedWebChatSession?> StartAsync(
        StartWebChatConversationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        PublicWebChatEndpoint? endpoint = await ResolveAsync(command.PublicKey, cancellationToken)
            .ConfigureAwait(false);

        if (endpoint is null)
        {
            return null;
        }

        // Both halves, every time. An endpoint somebody activated months ago is
        // no licence to talk to an agent that has since been switched off, and an
        // active agent is no licence to reach it through an endpoint that was
        // withdrawn.
        if (!IsServing(endpoint))
        {
            throw new WebChatUnavailableException();
        }

        DateTimeOffset now = _clock.GetUtcNow();

        // The channel is decided here, not by the caller. A visitor cannot claim
        // to be arriving over WhatsApp.
        Conversation conversation = Conversation.Start(
            endpoint.TenantId,
            ChannelKind.WebChat,
            now);

        NewWebChatAccessToken credential = _tokens.Create();

        // The agent is recorded on the session rather than followed through the
        // endpoint, so this conversation answers from the agent it started with
        // for the rest of its life.
        WebChatSession session = WebChatSession.Open(
            conversation.Id,
            endpoint.TenantId,
            endpoint.Id,
            endpoint.AiAgentId,
            credential.Hash,
            now);

        await _store
            .StartConversationAsync(conversation, session, cancellationToken)
            .ConfigureAwait(false);

        return new StartedWebChatSession(
            new StartedWebChatConversation(conversation.Id.Value, endpoint.AiAgentName),
            credential.Token);
    }

    private async Task<PublicWebChatEndpoint?> ResolveAsync(
        string? publicKey,
        CancellationToken cancellationToken)
    {
        // A malformed key is answered exactly as an unknown one, and without a
        // query. A public route is reached by anybody with a browser, and a typo
        // must not be distinguishable from a key that simply is not in use.
        if (!WebChatPublicKey.TryCreate(publicKey, out WebChatPublicKey key))
        {
            return null;
        }

        return await _endpoints
            .FindByPublicKeyAsync(key, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether this endpoint may serve the public right now.
    /// </summary>
    /// <remarks>
    /// The one place the rule is written. Both halves must hold: the endpoint
    /// published, and the agent behind it active. The runtime asks the same
    /// question again before every reply, because either half can change while a
    /// conversation is open.
    /// </remarks>
    private static bool IsServing(PublicWebChatEndpoint endpoint) =>
        endpoint.Status == WebChatEndpointStatus.Active
        && endpoint.AiAgentStatus == AiAgentStatus.Active;
}

/// <summary>A started conversation and the one copy of its credential.</summary>
/// <remarks>
/// <strong>This shape exists exactly once.</strong> No read path returns a
/// credential, and no other type in this feature has a field that could hold one,
/// so there is no response a future edit could widen into a disclosure. The edge
/// turns <see cref="AccessToken"/> into a cookie the browser cannot read and
/// never puts it in a body.
/// </remarks>
public sealed record StartedWebChatSession(
    StartedWebChatConversation Conversation,
    string AccessToken);
