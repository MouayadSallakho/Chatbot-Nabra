using OmniAI.Application.Ai;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.Knowledge;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Hosted;

/// <summary>A visitor's message, and the reply it is asking for.</summary>
/// <param name="ConversationId">The conversation the visitor named.</param>
/// <param name="AccessToken">The raw secret from the visitor's cookie.</param>
/// <param name="ClientMessageId">The client's own name for this message.</param>
/// <param name="Text">What the visitor typed.</param>
/// <param name="Client">
/// The pseudonymous network the request came from, established at the edge from
/// the connection. Nullable because the edge may be unable to establish one, in
/// which case the message is refused rather than counted in a shared bucket.
/// </param>
/// <param name="Mode">
/// Which transport this send arrived through. Set by the endpoint that received
/// the request and never by the caller, so a credential presented at the wrong
/// door is refused before anything is spent.
/// </param>
public sealed record SendWebChatMessageCommand(
    Guid ConversationId,
    string? AccessToken,
    string? ClientMessageId,
    string? Text,
    ClientNetworkKey? Client,
    WebChatClientMode Mode = WebChatClientMode.Hosted);

/// <summary>
/// One turn of a live website chat: store what was said, and answer it.
/// </summary>
/// <remarks>
/// <strong>This is the only place in the platform where an anonymous request
/// spends money.</strong> Everything unusual about it follows from that. A
/// duplicate must not buy a second reply; two messages at once must not buy two
/// replies to the same history; a provider failure must not lose what the
/// customer said; and none of it may be prevented by a mechanism that stops
/// working the moment there are two API instances.
/// <para>
/// <strong>The order of the steps is the design.</strong> Access is settled
/// before anything is read. Service state is checked before anything is written.
/// The idempotency record is consulted before the lock is taken, and again after
/// it — cheaply first for the common repeat, correctly second for the racing one.
/// The customer's message is committed before the provider is called, so it
/// survives the call failing. The reply is written only once a valid one exists.
/// </para>
/// <para>
/// <strong>No transaction spans the provider call.</strong> Each write is its own
/// short transaction. Holding one open across a network call to somebody else's
/// service would keep row locks and a connection for however many seconds that
/// service felt like taking, which under load is how a database runs out of
/// connections. The mutual exclusion that does span the call is an advisory lock,
/// which holds no rows and is released by the database itself if this process
/// dies.
/// </para>
/// <para>
/// <strong>There are two rate limits here and they are at different depths, on
/// purpose.</strong> The request limit is consumed at the top, before any
/// credential is believed, because its job is to make a flood of doomed requests
/// cheap. The generation budget is consumed at the very bottom — after the
/// idempotency check, inside the lock, after the second idempotency check —
/// because its job is to bound money, and only the one request that is about to
/// call a provider is spending any. Put the generation budget any higher and a
/// visitor whose browser retried a completed message would be charged for it
/// again; put the request limit any lower and every refusal would cost a
/// database round trip first.
/// </para>
/// <para>
/// <strong>Nothing is spent on a refused turn.</strong> When the generation
/// budget refuses, no customer message is stored, no turn row is written, and
/// <c>IAiTextGenerator</c> is not reached. That ordering is what makes "a 429
/// means zero provider calls" a property of the code rather than a hope: the
/// budget is consulted before <c>BeginTurnAsync</c>, and the provider is only
/// reachable through it.
/// </para>
/// </remarks>
public sealed class SendWebChatMessageHandler(
    PublicWebChatAccess access,
    IWebChatEndpointRepository endpoints,
    IAiAgentRepository agents,
    CustomerTurnRunner runner,
    IPublicChatProtection protection,
    WebChatPolicy policy,
    TimeProvider clock)
{
    private readonly PublicWebChatAccess _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly CustomerTurnRunner _runner =
        runner ?? throw new ArgumentNullException(nameof(runner));

    private readonly IPublicChatProtection _protection =
        protection ?? throw new ArgumentNullException(nameof(protection));

    private readonly WebChatPolicy _policy =
        policy ?? throw new ArgumentNullException(nameof(policy));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The stored message and its reply, or <see langword="null"/> when the
    /// credential does not open this conversation — which is also the answer for a
    /// conversation that does not exist.
    /// </returns>
    /// <exception cref="DomainValidationException">The message or its id is unusable.</exception>
    /// <exception cref="WebChatUnavailableException">The endpoint or agent is not serving.</exception>
    /// <exception cref="WebChatTurnInProgressException">The same message is already being answered.</exception>
    /// <exception cref="WebChatTurnAlreadyFailedException">The same message already failed.</exception>
    /// <exception cref="AiRuntimeUnavailableException">No AI runtime is configured.</exception>
    /// <exception cref="AiRuntimeTimeoutException">The provider did not answer in time.</exception>
    /// <exception cref="AiResponseInvalidException">The provider answered with nothing usable.</exception>
    /// <exception cref="PublicChatRateLimitedException">
    /// This network is asking too often, or this conversation or workspace has
    /// reached its generation budget. Nothing is stored and no provider is called.
    /// </exception>
    /// <exception cref="PublicChatProtectionUnavailableException">
    /// No trustworthy network identity, or no reachable protection store.
    /// </exception>
    public async Task<WebChatTurnResult?> HandleAsync(
        SendWebChatMessageCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        ConversationId conversationId = ConversationId.From(command.ConversationId);

        // Validated before the conversation is touched, so a malformed message is
        // refused identically whether the conversation is real or not.
        ClientMessageId clientMessageId = ClientMessageId.Create(command.ClientMessageId);
        string text = RequireAcceptableText(command.Text);

        // The cheap outer boundary, and deliberately the first thing that touches
        // shared state. It runs before the credential is checked, so a flood of
        // requests with a junk cookie is stopped by a counter rather than by a
        // query each -- and it therefore counts requests that turn out to be
        // unauthorised, which is exactly what an anti-flood limit should do.
        ClientNetworkKey client = RequireClient(command.Client);

        await RequireAllowedAsync(
                () => _protection.AuthoriseRequestAsync(client, cancellationToken))
            .ConfigureAwait(false);

        WebChatSession? session = await _access
            .AuthoriseAsync(conversationId, command.AccessToken, command.Mode, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return null;
        }

        // Re-checked on every turn rather than trusted from when the conversation
        // started. An endpoint can be withdrawn and an agent disabled at any
        // moment, and when they are, the public must stop being answered
        // immediately -- not at the end of whatever conversations are open.
        AiAgent agent = await RequireServingAgentAsync(session, cancellationToken)
            .ConfigureAwait(false);

        // Everything from here is the conversation runtime rather than this
        // transport: the idempotency replay, the advisory lock, the human-mode
        // check, the customer message, retrieval and generation. It lives in
        // CustomerTurnRunner so WhatsApp runs exactly the same code rather than
        // a copy of it that could drift.
        //
        // What stays here is what only website chat knows: the network key, the
        // per-client flood limit, the session credential, the serving endpoint,
        // and the shape of the answer a browser expects.
        CustomerTurnOutcome outcome = await _runner
            .RunAsync(
                new CustomerTurn(
                    session.TenantId,
                    conversationId,
                    agent,
                    clientMessageId,
                    text,
                    _clock.GetUtcNow()),

                // The expensive budget, consumed past both idempotency checks,
                // inside the lock, and only when a provider is genuinely about
                // to be called. Passed as a hook rather than built into the
                // runner because it meters anonymous public traffic, which is a
                // property of this transport and not of a conversation.
                beforeGeneration: token => RequireAllowedAsync(
                    () => _protection.AuthoriseGenerationAsync(
                        session.TenantId,
                        conversationId,
                        token)),
                cancellationToken)
            .ConfigureAwait(false);

        return ToTurnResult(outcome);
    }

    /// <summary>
    /// Shapes a runtime outcome into what a browser expects.
    /// </summary>
    /// <remarks>
    /// The one thing this handler still does with an answered turn. A visitor's
    /// client is told the customer's own message, the reply if there is one, and
    /// who is answering — never an employee, never a workspace.
    /// </remarks>
    private static WebChatTurnResult ToTurnResult(CustomerTurnOutcome outcome) =>
        new(
            ToView(outcome.CustomerMessage, MessageSender.Customer),
            outcome.Reply is null ? null : ToView(outcome.Reply, MessageSender.AiAgent),
            outcome.Handling.ToString());

    /// <summary>
    /// The agent this conversation is bound to, if it may still answer.
    /// </summary>
    /// <exception cref="WebChatUnavailableException">
    /// The endpoint was withdrawn, or the agent is no longer active.
    /// </exception>
    private async Task<AiAgent> RequireServingAgentAsync(
        WebChatSession session,
        CancellationToken cancellationToken)
    {
        Versioned<WebChatEndpoint>? endpoint = await _endpoints
            .FindAsync(session.TenantId, session.EndpointId, cancellationToken)
            .ConfigureAwait(false);

        if (endpoint is null || !endpoint.Value.IsServingPublicTraffic)
        {
            throw new WebChatUnavailableException();
        }

        // The agent recorded on the session, not the one the endpoint currently
        // names. They are the same today because an endpoint's agent cannot be
        // changed -- and this line is what keeps them the same if that ever
        // becomes possible.
        Versioned<AiAgent>? agent = await _agents
            .FindAsync(session.TenantId, session.AiAgentId, cancellationToken)
            .ConfigureAwait(false);

        if (agent is null || agent.Value.Status != AiAgentStatus.Active)
        {
            throw new WebChatUnavailableException();
        }

        return agent.Value;
    }

    /// <summary>
    /// Checks the message against the public bound before the Domain sees it.
    /// </summary>
    /// <remarks>
    /// The Domain's own limit is far higher and exists for different reasons. This
    /// one is about what an unauthenticated stranger may push into a workspace's
    /// database and into a paid call, and it refuses rather than trims: answering
    /// half of somebody's message is worse than telling them it was too long.
    /// </remarks>
    private string RequireAcceptableText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DomainValidationException("Message text is required.");
        }

        string trimmed = text.Trim();

        if (trimmed.Length > _policy.MaxIncomingMessageLength)
        {
            throw new DomainValidationException(
                $"A message must be at most {_policy.MaxIncomingMessageLength} characters; "
                + $"the supplied message has {trimmed.Length}. It is not truncated.");
        }

        return trimmed;
    }

    /// <summary>
    /// Asks the protection store, and turns a refusal into the refusal.
    /// </summary>
    /// <remarks>
    /// Written once rather than at each call site so that no future limit can be
    /// added and then checked with an <c>if</c> that forgets to throw. An
    /// unreachable store does not return from here at all: it raises
    /// <see cref="PublicChatProtectionUnavailableException"/> from inside the
    /// implementation, so there is no code path in which the answer is unknown and
    /// the request proceeds anyway.
    /// </remarks>
    private static async Task RequireAllowedAsync(Func<Task<PublicChatDecision>> ask)
    {
        PublicChatDecision decision = await ask().ConfigureAwait(false);

        if (!decision.Allowed)
        {
            throw new PublicChatRateLimitedException(decision.RetryAfter);
        }
    }

    /// <summary>
    /// The network this request came from, or a refusal.
    /// </summary>
    /// <remarks>
    /// Fails closed for the same reason starting a conversation does: a request
    /// with no establishable origin is one no per-network limit can bound, and an
    /// "unknown" bucket would be the one unmetered lane through the policy.
    /// </remarks>
    private static ClientNetworkKey RequireClient(ClientNetworkKey? client) =>
        client is ClientNetworkKey known && !known.IsEmpty
            ? known
            : throw new PublicChatProtectionUnavailableException(
                "No trustworthy client network identity could be established for a public web "
                + "chat write, so it was refused. Check the ForwardedHeaders trust "
                + "configuration and that the deployment sits behind the proxy it names.");

    private static PublicChatMessageView ToView(ConversationMessageRef message, MessageSender sender) =>
        new(message.Id, sender.ToString(), message.Text, message.SentAt);
}
