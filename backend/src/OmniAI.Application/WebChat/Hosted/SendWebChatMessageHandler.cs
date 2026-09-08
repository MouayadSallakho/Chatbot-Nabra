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
public sealed record SendWebChatMessageCommand(
    Guid ConversationId,
    string? AccessToken,
    string? ClientMessageId,
    string? Text,
    ClientNetworkKey? Client);

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
    IWebChatRuntimeStore store,
    IConversationRepository conversations,
    IAiAgentRepository agents,
    IAiTextGenerator generator,
    IConversationLock conversationLock,
    IPublicChatProtection protection,
    KnowledgeRetriever knowledge,
    ConversationContextPolicy context,
    WebChatPolicy policy,
    TimeProvider clock)
{
    private readonly PublicWebChatAccess _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IWebChatRuntimeStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IConversationRepository _conversations =
        conversations ?? throw new ArgumentNullException(nameof(conversations));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly IAiTextGenerator _generator =
        generator ?? throw new ArgumentNullException(nameof(generator));

    private readonly IConversationLock _lock =
        conversationLock ?? throw new ArgumentNullException(nameof(conversationLock));

    private readonly IPublicChatProtection _protection =
        protection ?? throw new ArgumentNullException(nameof(protection));

    private readonly KnowledgeRetriever _knowledge =
        knowledge ?? throw new ArgumentNullException(nameof(knowledge));

    private readonly ConversationContextPolicy _context =
        context ?? throw new ArgumentNullException(nameof(context));

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
            .AuthoriseAsync(conversationId, command.AccessToken, cancellationToken)
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

        // The cheap path for the overwhelmingly common duplicate: a retry of a
        // request whose answer was already produced. Answered without taking the
        // lock, so a repeat never waits behind a live turn.
        WebChatTurnResult? settled = await ReplayIfSettledAsync(
                conversationId,
                clientMessageId,
                cancellationToken)
            .ConfigureAwait(false);

        if (settled is not null)
        {
            return settled;
        }

        await using IAsyncDisposable hold = await _lock
            .AcquireAsync(conversationId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Asked again inside the lock. The first check raced with anything already
        // running; this one cannot, so two requests carrying the same id can never
        // both go on to call the provider.
        settled = await ReplayIfSettledAsync(conversationId, clientMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (settled is not null)
        {
            return settled;
        }

        // The expensive budget, consumed here and nowhere else: past both
        // idempotency checks and inside the lock, so this request is the one that
        // is genuinely about to call a provider. A duplicate never reaches this
        // line -- it returned a replay above -- which is what makes the same
        // message sent a hundred times cost one attempt.
        //
        // Before the customer's message is stored, so a refusal leaves no trace:
        // no message, no turn, and nothing claiming a generation happened.
        await RequireAllowedAsync(() => _protection.AuthoriseGenerationAsync(
                session.TenantId,
                conversationId,
                cancellationToken))
            .ConfigureAwait(false);

        DateTimeOffset now = _clock.GetUtcNow();

        // Committed before the provider is called. What the customer said is a
        // fact from the moment they said it, and it must outlive a failed reply.
        ConversationTurn? turn = await _store
            .BeginTurnAsync(conversationId, clientMessageId, text, now, cancellationToken)
            .ConfigureAwait(false);

        if (turn is null)
        {
            // The unique index refused it: a duplicate committed between the check
            // above and this write. Nothing has been spent and nothing stored.
            throw new WebChatTurnInProgressException();
        }

        return await AnswerAsync(session, agent, turn, text, now, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Produces the reply, and settles the turn either way.
    /// </summary>
    /// <remarks>
    /// The customer's message is already stored and committed when this begins, so
    /// every path out of here leaves a transcript that shows what they said.
    /// </remarks>
    private async Task<WebChatTurnResult> AnswerAsync(
        WebChatSession session,
        AiAgent agent,
        ConversationTurn turn,
        string text,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        AiTextGenerationResult result;

        try
        {
            // Inside the try, and that placement is the design. Building the
            // request now includes retrieving knowledge, which means embedding the
            // customer's message -- a call to a paid provider that can fail. When
            // it does, the turn has to end up durably failed like any other
            // provider failure: the customer's message stays, no reply is
            // invented, and the same client message id cannot silently buy another
            // attempt. Leaving this outside the try would have left the turn
            // pending forever and the visitor waiting on a reply that was never
            // coming.
            AiTextGenerationRequest request = await BuildRequestAsync(
                    session,
                    agent,
                    text,
                    cancellationToken)
                .ConfigureAwait(false);

            result = await _generator
                .GenerateAsync(request, cancellationToken)
                .ConfigureAwait(false);

            // The result type refuses blank text at construction, so an adapter
            // cannot build one. It could still return null, which no contract can
            // prevent, and a null dereference here would surface as an unexplained
            // fault rather than as the upstream breach it is.
            if (result is null)
            {
                throw new AiResponseInvalidException();
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Recorded as failed, and the failure is then re-raised unchanged so
            // the edge can translate it into the status the visitor deserves.
            // Nothing is invented: no reply is stored, and no placeholder message
            // appears in anybody's transcript.
            await _store
                .FailTurnAsync(turn, _clock.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }

        ConversationMessageRef reply = await _store
            .CompleteTurnAsync(turn, result.Text, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return new WebChatTurnResult(
            new PublicChatMessageView(
                turn.CustomerMessageId.Value,
                nameof(MessageSender.Customer),
                text,
                sentAt),
            ToView(reply, MessageSender.AiAgent));
    }

    /// <summary>
    /// Assembles the provider request from the agent's current configuration and
    /// the conversation so far.
    /// </summary>
    /// <remarks>
    /// <strong>The instructions are read now, not snapshotted when the
    /// conversation started.</strong> A workspace that corrects its agent's
    /// instructions expects the correction to take effect, and it does — from the
    /// next turn onwards. Earlier replies are left as they were said, because
    /// rewriting them would be rewriting history.
    /// <para>
    /// <strong>History is read in ingestion order and never sorted.</strong> The
    /// tail is fetched, bounded by the context policy, and handed to the policy in
    /// exactly the order the platform stored it.
    /// </para>
    /// </remarks>
    private async Task<AiTextGenerationRequest> BuildRequestAsync(
        WebChatSession session,
        AiAgent agent,
        string question,
        CancellationToken cancellationToken)
    {
        // One more row than the policy will keep, so the policy always decides the
        // boundary rather than inheriting whatever the query happened to return.
        IReadOnlyList<ConversationMessageRecord> history = await _conversations
            .ListRecentMessagesAsync(
                session.ConversationId,
                _context.MaxMessages + 1,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AiTurn> turns = _context.SelectTurns(history);

        if (turns.Count == 0)
        {
            // Unreachable in practice: the customer's message was committed a
            // moment ago and is always eligible. Raised rather than papered over,
            // because an empty request would ask a provider to answer nothing and
            // bill for it.
            throw new AiResponseInvalidException();
        }

        // Retrieved after the customer's message is committed and after the
        // generation budget has already been consumed, so a turn refused by Phase
        // 16 costs no embedding call either. The retriever asks whether this agent
        // has any eligible knowledge before it embeds anything, so an agent with
        // none -- which is every agent until somebody assigns some -- reaches the
        // provider exactly as it did before this feature existed.
        //
        // The customer's current message is the query. No rewriting, no history
        // summarisation, no second model call: each of those is a design decision
        // in its own right, and inventing one here would have been guessing.
        IReadOnlyList<KnowledgeExcerpt> excerpts = await _knowledge
            .RetrieveAsync(
                session.TenantId,
                session.AiAgentId,
                question,
                limit: null,
                cancellationToken)
            .ConfigureAwait(false);

        GroundingItem[] grounding =
        [
            .. excerpts.Select(excerpt =>
                GroundingItem.Create(excerpt.DocumentTitle, excerpt.Text)),
        ];

        // Three arguments, three privilege levels, and they never merge. The
        // instructions are the workspace's own configuration; the turns are what
        // was actually said; the grounding is untrusted reference material that
        // the adapter renders through a channel carrying no authority.
        return AiTextGenerationRequest.Create(agent.Instructions.Value, turns, grounding);
    }

    /// <summary>
    /// The turn's stored outcome, when this message has already been dealt with.
    /// </summary>
    /// <returns>
    /// The original result for a completed turn; <see langword="null"/> when this
    /// message is new and should be answered.
    /// </returns>
    private async Task<WebChatTurnResult?> ReplayIfSettledAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        CancellationToken cancellationToken)
    {
        ConversationTurn? existing = await _store
            .FindTurnAsync(conversationId, clientMessageId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return null;
        }

        switch (existing.Status)
        {
            case ConversationTurnStatus.Completed:
                ConversationMessageRef? reply = await _store
                    .FindReplyAsync(existing, cancellationToken)
                    .ConfigureAwait(false);

                ConversationMessageRef? customer = await _store
                    .FindCustomerMessageAsync(existing, cancellationToken)
                    .ConfigureAwait(false);

                if (reply is null || customer is null)
                {
                    // A completed turn always names both. Missing one means the
                    // rows have been tampered with, and answering again would
                    // spend money to cover it up.
                    throw new AiResponseInvalidException();
                }

                return new WebChatTurnResult(
                    ToView(customer, MessageSender.Customer),
                    ToView(reply, MessageSender.AiAgent));

            case ConversationTurnStatus.Failed:
                throw new WebChatTurnAlreadyFailedException();

            default:
                throw new WebChatTurnInProgressException();
        }
    }

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
