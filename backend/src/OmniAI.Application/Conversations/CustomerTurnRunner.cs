using OmniAI.Application.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.Knowledge;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Ai;

namespace OmniAI.Application.Conversations;

/// <summary>One customer turn, described without reference to any channel.</summary>
/// <param name="ClientMessageId">
/// The idempotency key. Website chat takes it from the browser; WhatsApp uses
/// Meta's message id, which serves the same purpose — the same value arriving
/// twice must produce one message and at most one generation.
/// </param>
public sealed record CustomerTurn(
    TenantId TenantId,
    ConversationId ConversationId,
    AiAgent Agent,
    ClientMessageId ClientMessageId,
    string Text,
    DateTimeOffset SentAt);

/// <summary>What a customer turn produced.</summary>
/// <param name="CustomerMessage">The message the customer sent, as stored.</param>
/// <param name="Reply">
/// The AI's answer, or <see langword="null"/> when a person is handling the
/// conversation and no reply was generated.
/// </param>
/// <param name="Handling">Who was answering when the turn ran.</param>
/// <param name="WasReplay">
/// <see langword="true"/> when this turn had already been settled and nothing
/// new was written. Channels use it to decide whether anything needs sending.
/// </param>
public sealed record CustomerTurnOutcome(
    ConversationMessageRef CustomerMessage,
    ConversationMessageRef? Reply,
    ConversationHandling Handling,
    bool WasReplay);

/// <summary>
/// Runs one customer turn: store what they said, and answer it if the AI is the
/// one answering.
/// </summary>
/// <remarks>
/// <strong>This is the whole of the conversation runtime that is not about a
/// transport.</strong> It was extracted from the website chat handler when
/// WhatsApp arrived, rather than copied — a second pipeline would be a second
/// place for the advisory lock, the idempotency replay, the human-mode check and
/// the retrieval policy to drift, and the drift would show up as one channel
/// quietly spending money the other does not, or answering a conversation a
/// colleague had taken over.
/// <para>
/// <strong>What stays outside.</strong> Everything a channel knows and this does
/// not: credentials, origins, sessions, network-level flood limits, message-size
/// bounds, and how to deliver the answer. A channel authorises its caller,
/// resolves a conversation, and hands over a <see cref="CustomerTurn"/>.
/// </para>
/// <para>
/// <strong>The generation budget is a hook, not a dependency.</strong> Website
/// chat meters anonymous public traffic against Phase 16's per-workspace budget;
/// a WhatsApp message has already been paid for in a different sense and arrives
/// through a signed webhook. Both need <em>a</em> decision before spending, and
/// neither should be forced to adopt the other's. The caller passes what applies
/// to it, or nothing.
/// </para>
/// </remarks>
public sealed class CustomerTurnRunner(
    IWebChatRuntimeStore store,
    IConversationRepository conversations,
    IAiTextGenerator generator,
    IConversationLock conversationLock,
    IConversationRealtimeNotifier realtime,
    KnowledgeRetriever knowledge,
    ConversationContextPolicy context,
    TimeProvider clock)
{
    private readonly IWebChatRuntimeStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IConversationRepository _conversations =
        conversations ?? throw new ArgumentNullException(nameof(conversations));

    private readonly IAiTextGenerator _generator =
        generator ?? throw new ArgumentNullException(nameof(generator));

    private readonly IConversationLock _lock =
        conversationLock ?? throw new ArgumentNullException(nameof(conversationLock));

    private readonly IConversationRealtimeNotifier _realtime =
        realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly KnowledgeRetriever _knowledge =
        knowledge ?? throw new ArgumentNullException(nameof(knowledge));

    private readonly ConversationContextPolicy _context =
        context ?? throw new ArgumentNullException(nameof(context));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Stores the customer's message and, in AI mode, answers it.
    /// </summary>
    /// <param name="beforeGeneration">
    /// Run inside the conversation lock, past both idempotency checks, and only
    /// when the AI is going to be called. A channel that meters spending raises
    /// its own refusal from here; one that does not passes
    /// <see langword="null"/>.
    /// </param>
    /// <exception cref="WebChatTurnInProgressException">
    /// The same key is already being answered.
    /// </exception>
    /// <exception cref="WebChatTurnAlreadyFailedException">
    /// The same key already failed. It is not retried, because a reply costs
    /// money and may have been half-produced.
    /// </exception>
    public async Task<CustomerTurnOutcome> RunAsync(
        CustomerTurn turn,
        Func<CancellationToken, Task>? beforeGeneration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        // The cheap path for the overwhelmingly common duplicate: a redelivery
        // of something already answered. Settled without taking the lock, so a
        // repeat never queues behind a live turn.
        CustomerTurnOutcome? settled = await ReplayAsync(turn, cancellationToken)
            .ConfigureAwait(false);

        if (settled is not null)
        {
            return settled;
        }

        await using IAsyncDisposable hold = await _lock
            .AcquireAsync(turn.ConversationId.Value, cancellationToken)
            .ConfigureAwait(false);

        // Asked again inside the lock. The first check raced with anything
        // already running; this one cannot, so two deliveries of one message can
        // never both call the provider.
        settled = await ReplayAsync(turn, cancellationToken).ConfigureAwait(false);

        if (settled is not null)
        {
            return settled;
        }

        // Who is answering, read inside the lock. Earlier would be reading a
        // value a takeover is free to change while this queues.
        ConversationHandling handling =
            await _store.ReadHandlingAsync(turn.ConversationId, cancellationToken)
                .ConfigureAwait(false)
            ?? ConversationHandling.Ai;

        bool answeredByAi = handling == ConversationHandling.Ai;

        if (answeredByAi && beforeGeneration is not null)
        {
            // Before the customer's message is stored, so a refusal leaves no
            // trace: no message, no turn, nothing claiming a generation happened.
            await beforeGeneration(cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset now = _clock.GetUtcNow();

        // Committed before the provider is called. What the customer said is a
        // fact from the moment they said it, and it must outlive a failed reply.
        ConversationTurn? opened = await _store
            .BeginTurnAsync(
                turn.ConversationId,
                turn.ClientMessageId,
                turn.Text,
                turn.SentAt,
                cancellationToken)
            .ConfigureAwait(false);

        if (opened is null)
        {
            // The unique index refused it: a duplicate committed between the
            // check above and this write. Nothing spent, nothing stored.
            throw new WebChatTurnInProgressException();
        }

        ConversationMessageRef customer = new(
            opened.CustomerMessageId.Value,
            turn.Text,
            turn.SentAt,
            IngestionSequence: 0);

        if (!answeredByAi)
        {
            await _store
                .SettleWithoutReplyAsync(opened, _clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);

            await Announce(turn, cancellationToken).ConfigureAwait(false);

            return new CustomerTurnOutcome(
                customer,
                Reply: null,
                ConversationHandling.Human,
                WasReplay: false);
        }

        return await AnswerAsync(turn, opened, customer, now, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Produces the reply, and settles the turn either way.</summary>
    private async Task<CustomerTurnOutcome> AnswerAsync(
        CustomerTurn turn,
        ConversationTurn opened,
        ConversationMessageRef customer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AiTextGenerationResult result;

        try
        {
            // Inside the try, and that placement is the design: building the
            // request retrieves knowledge, which embeds the customer's message
            // through a paid provider that can fail. When it does, the turn has
            // to end up durably failed like any other provider failure.
            AiTextGenerationRequest request =
                await BuildRequestAsync(turn, cancellationToken).ConfigureAwait(false);

            result = await _generator
                .GenerateAsync(
                    request,

                    // The workspace comes from the turn being answered, not from
                    // whatever request happens to be in flight.
                    AiUsageContext.For(turn.TenantId, AiUsagePurpose.ConversationGeneration),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result is null)
            {
                throw new AiResponseInvalidException();
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            await _store
                .FailTurnAsync(opened, _clock.GetUtcNow(), CancellationToken.None)
                .ConfigureAwait(false);

            throw;
        }

        ConversationMessageRef reply = await _store
            .CompleteTurnAsync(opened, result.Text, _clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        await Announce(turn, cancellationToken).ConfigureAwait(false);

        return new CustomerTurnOutcome(customer, reply, ConversationHandling.Ai, WasReplay: false);
    }

    /// <summary>
    /// Assembles the provider request from the agent's current configuration and
    /// the conversation so far.
    /// </summary>
    /// <remarks>
    /// Identical for every channel, which is the point. The instructions are read
    /// now rather than snapshotted; history is replayed in ingestion order and
    /// never sorted by time; retrieval is the ordinary knowledge runtime.
    /// </remarks>
    private async Task<AiTextGenerationRequest> BuildRequestAsync(
        CustomerTurn turn,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ConversationMessageRecord> history = await _conversations
            .ListRecentMessagesAsync(
                turn.ConversationId,
                _context.MaxMessages + 1,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AiTurn> turns = _context.SelectTurns(history);

        if (turns.Count == 0)
        {
            throw new AiResponseInvalidException();
        }

        IReadOnlyList<KnowledgeExcerpt> excerpts = await _knowledge
            .RetrieveAsync(turn.TenantId, turn.Agent.Id, turn.Text, limit: null, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<GroundingItem> grounding =
            [.. excerpts.Select(excerpt => GroundingItem.Create(excerpt.DocumentTitle, excerpt.Text))];

        return AiTextGenerationRequest.Create(turn.Agent.Instructions.Value, turns, grounding);
    }

    /// <summary>Replays a turn that is already settled, or answers null.</summary>
    private async Task<CustomerTurnOutcome?> ReplayAsync(
        CustomerTurn turn,
        CancellationToken cancellationToken)
    {
        ConversationTurn? existing = await _store
            .FindTurnAsync(turn.ConversationId, turn.ClientMessageId, cancellationToken)
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

                if (customer is null)
                {
                    // Every completed turn names the message that started it.
                    // Missing it means the rows have been tampered with.
                    throw new AiResponseInvalidException();
                }

                return new CustomerTurnOutcome(
                    customer,
                    reply,
                    reply is null ? ConversationHandling.Human : ConversationHandling.Ai,
                    WasReplay: true);

            case ConversationTurnStatus.Failed:
                throw new WebChatTurnAlreadyFailedException();

            default:
                throw new WebChatTurnInProgressException();
        }
    }

    private Task Announce(CustomerTurn turn, CancellationToken cancellationToken) =>
        _realtime.AnnounceQuietlyAsync(turn.TenantId, turn.ConversationId, cancellationToken);
}
