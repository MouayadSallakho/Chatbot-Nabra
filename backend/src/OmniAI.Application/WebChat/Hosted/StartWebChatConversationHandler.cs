using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Hosted;

/// <summary>Read the public face of one hosted chat endpoint.</summary>
/// <param name="PublicKey">The key from the page's address.</param>
/// <param name="Language">
/// The page's language (Phase 39.1), or null when it named none this platform
/// knows: then the fallback greeting, exactly as before.
/// </param>
public sealed record GetPublicWebChatQuery(string? PublicKey, Domain.Users.UserLanguage? Language = null);

/// <summary>Start a conversation on one hosted chat endpoint.</summary>
/// <remarks>
/// <strong>It carries a key and a network identity, and nothing else.</strong>
/// There is no tenant, no agent and no channel in this command, because a visitor
/// must not be able to choose any of them. The server reads all three from the
/// endpoint the key names, which is the only reason a public URL cannot be edited
/// into a request against somebody else's workspace.
/// </remarks>
/// <param name="PublicKey">The key from the page's address.</param>
/// <param name="Client">
/// The pseudonymous network the request came from, established at the edge from
/// the connection rather than from anything the caller said. Nullable because the
/// edge may genuinely be unable to establish one -- and the handler then refuses,
/// rather than treating "unknown" as a bucket of its own.
/// </param>
public sealed record StartWebChatConversationCommand(
    string? PublicKey,
    ClientNetworkKey? Client);

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
    IPublicChatProtection protection,
    PublicChatProtectionOptions limits,
    TimeProvider clock)
{
    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IWebChatRuntimeStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IWebChatAccessTokens _tokens =
        tokens ?? throw new ArgumentNullException(nameof(tokens));

    private readonly IPublicChatProtection _protection =
        protection ?? throw new ArgumentNullException(nameof(protection));

    private readonly PublicChatProtectionOptions _limits =
        limits ?? throw new ArgumentNullException(nameof(limits));

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
            IsServing(endpoint))
        {
            WorkspaceName = endpoint.WorkspaceName,
            Greeting = endpoint.Widget.GreetingFor(query.Language)?.Value,
            AccentColor = endpoint.Widget.Accent.Value,
            Theme = endpoint.Widget.Theme.ToString(),
        };
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
    /// <exception cref="PublicChatRateLimitedException">
    /// This network, or this endpoint in aggregate, is starting conversations too
    /// quickly.
    /// </exception>
    /// <exception cref="PublicChatProtectionUnavailableException">
    /// No trustworthy network identity, or no reachable protection store. Nothing
    /// is created.
    /// </exception>
    public async Task<StartedWebChatSession?> StartAsync(
        StartWebChatConversationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Fail closed before anything else. A request whose origin cannot be
        // established is a request no per-network limit can bound, and creating a
        // conversation row for it would be creating exactly the thing the limits
        // exist to bound.
        ClientNetworkKey client = RequireClient(command.Client);

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

        // After the endpoint is known, because the aggregate limit is per
        // endpoint -- and before anything is written, because this is the limit on
        // writing. The two counters behind it are consumed together or not at all.
        PublicChatDecision decision = await _protection
            .AuthoriseConversationStartAsync(client, endpoint.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!decision.Allowed)
        {
            throw new PublicChatRateLimitedException(decision.RetryAfter);
        }

        DateTimeOffset now = _clock.GetUtcNow();

        // The channel is decided here, not by the caller. A visitor cannot claim
        // to be arriving over WhatsApp.
        // A public visitor arrives with no identity this platform has ever seen,
        // so a new conversation is a new customer. That is the honest answer: the
        // web transport has no durable identity to key on, and manufacturing one
        // from a browser marker, a network address or a device signature would be
        // exactly the fingerprint-based identity resolution this platform refuses
        // to do. Two visits by the same person become one customer only when an
        // administrator says so.
        Customer customer = Customer.Create(
            endpoint.TenantId,
            displayName: null,
            now);

        Conversation conversation = Conversation.Start(
            endpoint.TenantId,
            customer.Id,
            ChannelKind.WebChat,
            now);

        NewWebChatAccessToken credential = _tokens.Create();

        // The agent is recorded on the session rather than followed through the
        // endpoint, so this conversation answers from the agent it started with
        // for the rest of its life.
        //
        // The expiry is computed once, here, and stored. Every later check reads
        // the stored instant, so this credential's life is fixed at the moment it
        // is issued rather than floating with whatever the deployment happens to
        // be configured for when somebody next sends a message.
        WebChatSession session = WebChatSession.Open(
            conversation.Id,
            endpoint.TenantId,
            endpoint.Id,
            endpoint.AiAgentId,
            credential.Hash,
            now,
            now + _limits.SessionLifetime);

        await _store
            .StartConversationAsync(conversation, customer, session, cancellationToken)
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

    /// <summary>
    /// The network this request came from, or a refusal.
    /// </summary>
    /// <remarks>
    /// <strong>Unknown is not a bucket.</strong> Putting every request with no
    /// establishable origin into one shared counter would make that counter the
    /// cheapest lane through the whole policy: an attacker who can suppress
    /// whatever the edge reads gets an allowance shared with nobody else's real
    /// traffic. Refusing costs a handful of genuinely unusual requests and closes
    /// the lane.
    /// </remarks>
    private static ClientNetworkKey RequireClient(ClientNetworkKey? client) =>
        client is ClientNetworkKey known && !known.IsEmpty
            ? known
            : throw new PublicChatProtectionUnavailableException(
                "No trustworthy client network identity could be established for a public web "
                + "chat write, so it was refused. Check the ForwardedHeaders trust "
                + "configuration and that the deployment sits behind the proxy it names.");
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
