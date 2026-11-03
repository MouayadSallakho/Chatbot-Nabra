using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Embedded;

/// <param name="PublicKey">The endpoint the widget was installed with. Public by design.</param>
/// <param name="ParentOrigin">
/// The website the widget is embedded on, as the browser reported it to the iframe
/// through <c>postMessage</c>'s <c>event.origin</c>. Never taken from a query
/// parameter the embedding page could choose.
/// </param>
public sealed record GetEmbeddedWebChatQuery(string? PublicKey, string? ParentOrigin);

/// <param name="Client">The pseudonymous network identity Phase 16 derives.</param>
public sealed record StartEmbeddedConversationCommand(
    string? PublicKey,
    string? ParentOrigin,
    ClientNetworkKey? Client);

/// <summary>What a widget needs before anybody has said anything.</summary>
/// <remarks>
/// Agent name, the appearance the workspace configured, and whether the endpoint
/// is currently serving. No workspace id, no agent id, no knowledge, no
/// credential: the widget renders a launcher and a panel, and none of those
/// require identifiers a page has no business holding.
/// </remarks>
public sealed record EmbeddedWebChatView(
    string AgentName,
    bool IsAvailable,
    string? Greeting,
    string AccentColor,
    string LauncherPosition,
    string Theme)
{
    /// <summary>The workspace's display name (Phase 37.5a, owner decision D-3).</summary>
    public string WorkspaceName { get; init; } = string.Empty;
}

/// <summary>A started embedded conversation, and the one time its credential exists.</summary>
public sealed record StartedEmbeddedConversation(
    Guid ConversationId,
    string AgentName,
    string Credential);

/// <summary>
/// The embedded widget's own bootstrap and conversation start.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is a doorway, not a second runtime.</strong> Reading the
/// transcript and sending a message go through exactly the same handlers the
/// hosted page uses, with the same protection, the same idempotency, the same
/// advisory lock and the same optional retrieval. What is different is only how a
/// request proves which conversation it may act on, and where it is allowed to be
/// embedded.
/// </para>
/// <para>
/// <strong>The origin decides whether anything happens at all.</strong> It is
/// checked before a conversation is created, before the protection layer is asked
/// to spend a start, and before any credential exists. A widget on an unapproved
/// website gets the same answer as one quoting a public key that was never issued:
/// nothing, with no hint about which of the two it was.
/// </para>
/// <para>
/// <strong>What this is not.</strong> An allowlist is a browser embedding policy.
/// The browser is what guarantees the origin an iframe sees, so it decides which
/// pages may run the widget — and it says nothing at all about a script speaking
/// HTTP directly, which can send any header it likes. Cost and abuse are bounded
/// by the distributed protection layer, which this passes through unchanged.
/// </para>
/// </remarks>
public sealed class EmbeddedWebChatHandler(
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
    /// Tells an approved widget what it is and how it should look.
    /// </summary>
    /// <remarks>
    /// Creates nothing. Loading a customer's page, and opening the widget on it,
    /// both end here — no conversation, no session, no credential, and no row of
    /// any kind. A conversation appears when somebody actually sends something.
    /// </remarks>
    public async Task<EmbeddedWebChatView?> GetAsync(
        GetEmbeddedWebChatQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Approved? approved = await ApproveAsync(
                query.PublicKey,
                query.ParentOrigin,
                cancellationToken)
            .ConfigureAwait(false);

        if (approved is null)
        {
            return null;
        }

        WebChatWidgetSettings widget = approved.Endpoint.Widget;

        return new EmbeddedWebChatView(
            approved.AgentName,
            approved.IsServing,
            widget.Greeting?.Value,
            widget.Accent.Value,
            widget.Position.ToString(),
            widget.Theme.ToString())
        {
            WorkspaceName = approved.WorkspaceName,
        };
    }

    /// <summary>
    /// Opens a conversation for an embedded widget, and issues its credential once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order matters and mirrors the hosted start exactly: resolve, check the
    /// website, check the endpoint is serving, then ask the protection layer
    /// whether this network may start another conversation. Only then is anything
    /// written.
    /// </para>
    /// <para>
    /// The returned credential is the only moment the raw value exists outside the
    /// browser that will hold it. Nothing stores it — the session keeps a SHA-256
    /// digest — and nothing logs it.
    /// </para>
    /// </remarks>
    public async Task<StartedEmbeddedConversation?> StartAsync(
        StartEmbeddedConversationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ClientNetworkKey client = RequireClient(command.Client);

        Approved? approved = await ApproveAsync(
                command.PublicKey,
                command.ParentOrigin,
                cancellationToken)
            .ConfigureAwait(false);

        if (approved is null)
        {
            return null;
        }

        if (!approved.IsServing)
        {
            throw new WebChatUnavailableException();
        }

        PublicChatDecision decision = await _protection
            .AuthoriseConversationStartAsync(client, approved.Endpoint.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!decision.Allowed)
        {
            throw new PublicChatRateLimitedException(decision.RetryAfter);
        }

        DateTimeOffset now = _clock.GetUtcNow();

        // A public visitor arrives with no identity this platform has ever seen,
        // so a new conversation is a new customer. That is the honest answer: the
        // web transport has no durable identity to key on, and manufacturing one
        // from a browser marker, a network address or a device signature would be
        // exactly the fingerprint-based identity resolution this platform refuses
        // to do. Two visits by the same person become one customer only when an
        // administrator says so.
        Customer customer = Customer.Create(
            approved.Endpoint.TenantId,
            displayName: null,
            now);

        Conversation conversation = Conversation.Start(
            approved.Endpoint.TenantId,
            customer.Id,
            ChannelKind.WebChat,
            now);

        NewWebChatAccessToken credential = _tokens.Create();

        WebChatSession session = WebChatSession.OpenEmbedded(
            conversation.Id,
            approved.Endpoint.TenantId,
            approved.Endpoint.Id,
            approved.Endpoint.AiAgentId,
            credential.Hash,
            approved.Origin,
            now,
            now + _limits.SessionLifetime);

        await _store
            .StartConversationAsync(conversation, customer, session, cancellationToken)
            .ConfigureAwait(false);

        return new StartedEmbeddedConversation(
            conversation.Id.Value,
            approved.AgentName,
            credential.Token);
    }

    /// <summary>An endpoint that exists and has approved this website.</summary>
    private sealed record Approved(
        Domain.WebChat.WebChatEndpoint Endpoint,
        WebChatOrigin Origin,
        string AgentName,
        bool IsServing,
        string WorkspaceName);

    /// <summary>
    /// Resolves the endpoint and decides whether this website may embed it.
    /// </summary>
    /// <remarks>
    /// A single nothing for every reason it might fail: a malformed key, an
    /// unknown key, a malformed origin, or a website nobody approved. Telling them
    /// apart would let somebody with a public key discover which sites a workspace
    /// has authorised, which is exactly the list they would need to attack.
    /// </remarks>
    private async Task<Approved?> ApproveAsync(
        string? publicKey,
        string? parentOrigin,
        CancellationToken cancellationToken)
    {
        if (!WebChatPublicKey.TryCreate(publicKey, out WebChatPublicKey key))
        {
            return null;
        }

        if (!WebChatOrigin.TryCreate(parentOrigin, out WebChatOrigin origin, out _))
        {
            return null;
        }

        PublicWebChatEndpoint? published = await _endpoints
            .FindByPublicKeyAsync(key, cancellationToken)
            .ConfigureAwait(false);

        if (published is null)
        {
            return null;
        }

        // The aggregate is read for its allowlist. The published projection is
        // what carries the agent's name and status, so both are needed -- and
        // reading the aggregate second means an unknown key costs one query.
        Versioned<Domain.WebChat.WebChatEndpoint>? endpoint = await _endpoints
            .FindAsync(published.TenantId, published.Id, cancellationToken)
            .ConfigureAwait(false);

        if (endpoint is null || !endpoint.Value.Allows(origin))
        {
            return null;
        }

        return new Approved(
            endpoint.Value,
            origin,
            published.AiAgentName,
            published.Status == WebChatEndpointStatus.Active
            && published.AiAgentStatus == AiAgentStatus.Active,
            published.WorkspaceName);
    }

    private static ClientNetworkKey RequireClient(ClientNetworkKey? client) =>
        client is ClientNetworkKey known && !known.IsEmpty
            ? known
            : throw new PublicChatProtectionUnavailableException(
                "No trustworthy client network identity could be established for an embedded "
                + "web chat write, so it was refused. Check the ForwardedHeaders trust "
                + "configuration and that the deployment sits behind the proxy it names.");
}
