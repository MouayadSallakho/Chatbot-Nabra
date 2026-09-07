namespace OmniAI.Domain.Conversations;

/// <summary>
/// One customer message and the reply it asked for, as a durable record.
/// </summary>
/// <remarks>
/// <strong>Why a turn exists at all.</strong> Appending a message is instant;
/// answering it is a call to somebody else's service that takes seconds and can
/// fail. Between those two facts sits everything that can go wrong — a repeated
/// request, a lost response, a provider timeout — and none of it can be reasoned
/// about if the only trace of the exchange is the messages themselves. This
/// record is the exchange: what the client called it, which customer message it
/// stored, whether a reply was produced, and which one.
/// <para>
/// <strong>It is not a workflow engine.</strong> There are no steps, no
/// retries, no scheduling, no compensation and no queue. Three states and two
/// message references, because that is what idempotency and honest failure
/// reporting require and nothing more has been justified.
/// </para>
/// <para>
/// <strong>The customer's message is recorded before the reply is attempted, and
/// survives its failure.</strong> A turn that ends <see cref="ConversationTurnStatus.Failed"/>
/// still names a stored customer message. Nothing rolls that message back, because
/// the customer did say it and a transcript that quietly loses what somebody typed
/// is worse than one that shows an unanswered message.
/// </para>
/// </remarks>
public sealed class ConversationTurn
{
    private ConversationTurn(
        ConversationTurnId id,
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        MessageId customerMessageId,
        DateTimeOffset createdAt)
    {
        Id = id;
        ConversationId = conversationId;
        ClientMessageId = clientMessageId;
        CustomerMessageId = customerMessageId;
        Status = ConversationTurnStatus.Generating;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>Identity.</summary>
    public ConversationTurnId Id { get; }

    /// <summary>The conversation this turn belongs to.</summary>
    public ConversationId ConversationId { get; }

    /// <summary>
    /// The client's name for the message. Unique within the conversation.
    /// </summary>
    /// <remarks>
    /// Uniqueness is enforced by the database, not by a check here: two concurrent
    /// requests carrying the same value would both pass any in-process test, and
    /// the only place that can settle the race is the index.
    /// </remarks>
    public ClientMessageId ClientMessageId { get; }

    /// <summary>The stored customer message this turn was started by.</summary>
    public MessageId CustomerMessageId { get; }

    /// <summary>The stored reply, once there is one.</summary>
    public MessageId? ReplyMessageId { get; private set; }

    /// <summary>How far the turn got.</summary>
    public ConversationTurnStatus Status { get; private set; }

    /// <summary>When the turn was started, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the turn last changed, in UTC. Never moves backwards.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether this turn is finished, either way.</summary>
    public bool IsSettled => Status != ConversationTurnStatus.Generating;

    /// <summary>Begins a turn for a customer message that has just been stored.</summary>
    /// <exception cref="DomainValidationException">
    /// An identifier is empty or the instant is not a real point in time.
    /// </exception>
    public static ConversationTurn Begin(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        MessageId customerMessageId,
        DateTimeOffset startedAt)
    {
        if (conversationId.IsEmpty)
        {
            throw new DomainValidationException("A conversation turn must name a conversation.");
        }

        if (clientMessageId.IsEmpty)
        {
            throw new DomainValidationException("A conversation turn must carry a client message id.");
        }

        if (customerMessageId.IsEmpty)
        {
            throw new DomainValidationException("A conversation turn must name a customer message.");
        }

        return new ConversationTurn(
            ConversationTurnId.New(),
            conversationId,
            clientMessageId,
            customerMessageId,
            RequireInstant(startedAt));
    }

    /// <summary>Reconstitutes a stored turn. For persistence only.</summary>
    public static ConversationTurn FromStored(
        ConversationTurnId id,
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        MessageId customerMessageId,
        MessageId? replyMessageId,
        ConversationTurnStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(id, conversationId, clientMessageId, customerMessageId, createdAt)
        {
            ReplyMessageId = replyMessageId,
            Status = status,
            UpdatedAt = updatedAt,
        };

    /// <summary>Records that a reply was produced and stored.</summary>
    /// <exception cref="DomainStateException">The turn is already settled.</exception>
    /// <exception cref="DomainValidationException">
    /// The reply names nothing, or the instant is not a real point in time.
    /// </exception>
    public void Complete(MessageId replyMessageId, DateTimeOffset completedAt)
    {
        RequireUnsettled("complete");

        if (replyMessageId.IsEmpty)
        {
            throw new DomainValidationException("A completed turn must name its reply message.");
        }

        ReplyMessageId = replyMessageId;
        Status = ConversationTurnStatus.Completed;
        RecordChange(RequireInstant(completedAt));
    }

    /// <summary>
    /// Records that no reply was produced.
    /// </summary>
    /// <remarks>
    /// Deliberately carries no reason, no provider message and no diagnostic. A
    /// turn is readable by the visitor whose conversation it belongs to, and a
    /// provider's error text is operational detail that belongs in a log, not in
    /// somebody's chat history. What the caller is told about the failure is
    /// decided at the edge, from the exception, not from this record.
    /// </remarks>
    /// <exception cref="DomainStateException">The turn is already settled.</exception>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Fail(DateTimeOffset failedAt)
    {
        RequireUnsettled("fail");

        Status = ConversationTurnStatus.Failed;
        RecordChange(RequireInstant(failedAt));
    }

    private void RequireUnsettled(string operation)
    {
        if (IsSettled)
        {
            throw new DomainStateException(
                $"Cannot {operation} a turn that is already {Status.ToString().ToLowerInvariant()}.");
        }
    }

    private void RecordChange(DateTimeOffset instant)
    {
        if (instant > UpdatedAt)
        {
            UpdatedAt = instant;
        }
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant)
    {
        if (instant == default)
        {
            throw new DomainValidationException("The instant must be a real point in time.");
        }

        return instant.ToUniversalTime();
    }
}
