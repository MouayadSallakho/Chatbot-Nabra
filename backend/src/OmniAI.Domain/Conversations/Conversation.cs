using System.Collections.ObjectModel;
using System.Globalization;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Conversations;

/// <summary>
/// An exchange between one tenant's business and one end customer, on one
/// channel. The aggregate root that owns its message history.
/// </summary>
/// <remarks>
/// This is a platform concept, not a provider's. It is not an OpenAI thread and
/// not a WhatsApp chat; it is what the business means by "a conversation", and
/// the channel it happens to run on is one attribute of it. No provider type,
/// endpoint, credential or external identifier appears here.
/// <para>
/// The aggregate keeps its history private and hands out a read-only view, so
/// message history cannot be rewritten from outside. Messages are created only
/// through this type, which is what guarantees every message belongs to exactly
/// one conversation.
/// </para>
/// </remarks>
public sealed class Conversation
{
    private readonly List<Message> _messages = [];
    private readonly ReadOnlyCollection<Message> _messagesView;

    private Conversation(
        ConversationId id,
        TenantId tenantId,
        CustomerId customerId,
        ChannelKind channel,
        DateTimeOffset startedAt)
    {
        Id = id;
        TenantId = tenantId;
        CustomerId = customerId;
        Channel = channel;
        Status = ConversationStatus.Open;
        Handling = ConversationHandling.Ai;
        StartedAt = startedAt;
        LastActivityAt = startedAt;
        _messagesView = _messages.AsReadOnly();
    }

    /// <summary>Identity. Fixed for the lifetime of the conversation.</summary>
    public ConversationId Id { get; }

    /// <summary>
    /// The owning tenant. Deliberately has no setter of any kind, not even a
    /// private one: a conversation belongs to exactly one tenant for its whole
    /// life and can never be moved to another. Tenant ownership is the platform's
    /// isolation boundary, so making it structurally immutable removes a whole
    /// class of cross-tenant leak.
    /// </summary>
    public TenantId TenantId { get; }

    /// <summary>
    /// Who the workspace is talking to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Required, so that "every conversation belongs to somebody" is an
    /// invariant rather than a hope.</strong> It is supplied at the transport
    /// boundary that already knows the relationship — the web chat session being
    /// opened, the WhatsApp number being routed — and written in the same
    /// transaction as the conversation itself, so there is no window in which a
    /// conversation exists without an owner.
    /// </para>
    /// <para>
    /// It has a private setter for exactly one operation: an administrator
    /// merging two customers, which moves this conversation to the canonical
    /// record. Nothing else may move a conversation between people, and nothing
    /// automatic may do it at all.
    /// </para>
    /// </remarks>
    public CustomerId CustomerId { get; private set; }

    /// <summary>The channel this conversation takes place on. Fixed at creation.</summary>
    public ChannelKind Channel { get; }

    /// <summary>Current lifecycle state.</summary>
    public ConversationStatus Status { get; private set; }

    /// <summary>
    /// Who answers new customer turns: the AI, or a person.
    /// </summary>
    /// <remarks>
    /// Starts at <see cref="ConversationHandling.Ai"/> because that is what a
    /// conversation on a chat endpoint is for. Nothing about a conversation
    /// arriving implies a person should look at it.
    /// </remarks>
    public ConversationHandling Handling { get; private set; }

    /// <summary>
    /// The member this conversation is routed to, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <strong>Routing, not authority.</strong> Being assigned grants nothing:
    /// a member who cannot reply still cannot reply, and a member who can reply
    /// can reply to a conversation assigned to somebody else. It is how a team
    /// says "this one is mine" so two people do not answer the same customer,
    /// and it is deliberately optional — taking a conversation over without
    /// claiming it is a normal thing to do.
    /// <para>
    /// It is independent of <see cref="Handling"/>. Assigning does not take a
    /// conversation over, and taking one over does not assign it.
    /// </para>
    /// </remarks>
    public UserId? AssignedTo { get; private set; }

    /// <summary>When the conversation began, in UTC.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// The high-water mark of activity on this conversation -- a message, a close
    /// or a reopen -- in UTC. Never moves backwards: adding a message that was
    /// sent earlier than the current value leaves it unchanged.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>
    /// The message history in <b>insertion order</b> -- the order in which this
    /// platform ingested the messages.
    /// </summary>
    /// <remarks>
    /// Insertion order is <b>not</b> a guarantee of external chronological order.
    /// This is a distributed omnichannel system: webhook delay and retries,
    /// provider redelivery, queue retries, concurrent processing, provider-side
    /// ordering and bulk imports all mean a message can arrive after one that was
    /// sent later. The aggregate records what it was given, in the order it was
    /// given, and never reorders or sorts. Callers that need chronological order
    /// sort by <see cref="Message.SentAt"/> themselves.
    /// <para>
    /// A genuine read-only wrapper, not the internal list typed as a read-only
    /// interface -- that would let a caller cast back to the concrete list type
    /// and edit history. The view is live: messages added later appear in it.
    /// </para>
    /// <para>
    /// Database ordering, indexing and API pagination are persistence and
    /// transport concerns, deferred.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Message> Messages => _messagesView;

    /// <summary>
    /// Starts a conversation. It begins <see cref="ConversationStatus.Open"/>.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="customerId">
    /// Who the workspace is talking to. Resolved by the transport boundary before
    /// this is called, because only that boundary knows whether this is somebody
    /// already known or somebody new.
    /// </param>
    /// <param name="channel">The channel it takes place on.</param>
    /// <param name="startedAt">The effective start instant, supplied by the caller.</param>
    /// <exception cref="DomainValidationException">
    /// The tenant id is empty, the customer id is empty, the channel is not a
    /// recognised value, or the instant is not a real point in time.
    /// </exception>
    public static Conversation Start(
        TenantId tenantId,
        CustomerId customerId,
        ChannelKind channel,
        DateTimeOffset startedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A conversation must belong to a tenant.")
            {
                Code = "conversations.tenant.required",
            };
        }

        if (customerId.IsEmpty)
        {
            throw new DomainValidationException("A conversation must belong to a customer.")
            {
                Code = "conversations.customer.required",
            };
        }

        RequireDefined(channel, "channel");

        return new Conversation(
            ConversationId.New(),
            tenantId,
            customerId,
            channel,
            RequireInstant(startedAt, "start instant"));
    }

    /// <summary>
    /// Moves this conversation to the customer a merge made canonical.
    /// </summary>
    /// <remarks>
    /// <strong>The only way a conversation changes hands, and it is not a
    /// re-attribution.</strong> A merge is an administrator stating that two
    /// records were one person all along, so this does not say the conversation
    /// happened with somebody else — it says the platform now knows their real
    /// name for that person. Nothing about the transcript, its ordering, its
    /// timestamps or its activity high-water mark is touched.
    /// </remarks>
    /// <exception cref="DomainValidationException">The customer id is empty.</exception>
    public void MoveToCustomer(CustomerId customerId)
    {
        if (customerId.IsEmpty)
        {
            throw new DomainValidationException("A conversation must belong to a customer.")
            {
                Code = "conversations.customer.required",
            };
        }

        CustomerId = customerId;
    }

    /// <summary>
    /// Appends a text message from a customer, an AI agent or a human agent.
    /// </summary>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">
    /// The sender is unrecognised or is <see cref="MessageSender.System"/>, the
    /// instant is not a real point in time, or the text breaks a content rule.
    /// </exception>
    public Message AddTextMessage(MessageSender sender, string? text, DateTimeOffset sentAt)
    {
        RequireOpen("add a message");
        RequireDefined(sender, "sender");

        if (sender == MessageSender.System)
        {
            throw new DomainValidationException(
                "A system-authored message must be added with AddSystemMessage, " +
                "so that its sender and content type stay consistent.")
            {
                Code = "conversations.sender.invalid",
            };
        }

        return Append(sender, MessageContentType.Text, text, sentAt);
    }

    /// <summary>
    /// Appends a platform-authored note recording something that happened -- the
    /// audit trail of the conversation itself.
    /// </summary>
    /// <remarks>
    /// This is not application logging, and it is not a place for provider
    /// payloads, credentials or diagnostics: its content is part of the
    /// conversation and may be shown to anyone who can read the conversation.
    /// </remarks>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">The instant or text is invalid.</exception>
    public Message AddSystemMessage(string? text, DateTimeOffset sentAt)
    {
        RequireOpen("add a message");

        return Append(MessageSender.System, MessageContentType.System, text, sentAt);
    }

    /// <summary>
    /// Closes the conversation. Idempotent: closing an already closed
    /// conversation is a no-op, because the caller's intent is already satisfied
    /// and demanding a check-then-act sequence would invite a race while
    /// preventing nothing.
    /// </summary>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Close(DateTimeOffset closedAt)
    {
        DateTimeOffset instant = RequireInstant(closedAt, "close instant");

        if (Status == ConversationStatus.Closed)
        {
            return;
        }

        Status = ConversationStatus.Closed;
        RecordActivity(instant);
    }

    /// <summary>
    /// Reopens the conversation so it accepts messages again. Idempotent, for the
    /// same reason as <see cref="Close"/>.
    /// </summary>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public void Reopen(DateTimeOffset reopenedAt)
    {
        DateTimeOffset instant = RequireInstant(reopenedAt, "reopen instant");

        if (Status == ConversationStatus.Open)
        {
            return;
        }

        Status = ConversationStatus.Open;
        RecordActivity(instant);
    }

    /// <summary>
    /// Hands the conversation to a person. Idempotent.
    /// </summary>
    /// <remarks>
    /// From here the AI answers nothing. A customer turn arriving after this is
    /// still recorded — the customer really did say it, and losing it because
    /// nobody had replied yet would be the worst possible failure — but no
    /// retrieval runs and no generation is paid for.
    /// <para>
    /// Idempotent for the same reason <see cref="Close"/> is: two people pressing
    /// Take over within a second of each other both meant the same thing, and
    /// making the second one an error would be reporting a problem that does not
    /// exist. It returns whether it changed anything, so a caller that wants to
    /// announce the change can tell.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> if the AI was answering until now.</returns>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public bool TakeOverByHuman(DateTimeOffset takenOverAt)
    {
        RequireOpen("take over");

        DateTimeOffset instant = RequireInstant(takenOverAt, "take-over instant");

        if (Handling == ConversationHandling.Human)
        {
            return false;
        }

        Handling = ConversationHandling.Human;
        RecordActivity(instant);

        return true;
    }

    /// <summary>
    /// Gives the conversation back to the AI. Idempotent.
    /// </summary>
    /// <remarks>
    /// <strong>It answers nothing retroactively.</strong> Customer messages that
    /// arrived while a person was handling this conversation stay unanswered by
    /// the AI, for ever. Generating replies to them now would be answering
    /// questions a colleague may already have answered by phone, in a transcript
    /// the customer is still reading — several minutes late, out of order, and
    /// paid for. Only a genuinely new customer turn starts the AI again.
    /// </remarks>
    /// <returns><see langword="true"/> if a person was handling it until now.</returns>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public bool ReturnToAi(DateTimeOffset returnedAt)
    {
        RequireOpen("return to the AI");

        DateTimeOffset instant = RequireInstant(returnedAt, "return instant");

        if (Handling == ConversationHandling.Ai)
        {
            return false;
        }

        Handling = ConversationHandling.Ai;
        RecordActivity(instant);

        return true;
    }

    /// <summary>
    /// Hands the conversation to a person, and says who did it and where.
    /// </summary>
    /// <param name="actor">The member taking it over.</param>
    /// <param name="takenOverAt">When.</param>
    /// <param name="afterSequence">
    /// The ingestion sequence of the newest message, read under the conversation
    /// lock; zero when there is none yet.
    /// </param>
    /// <returns>
    /// The event to store, or <see langword="null"/> when a person was already
    /// handling it: an idempotent press changes nothing and records nothing.
    /// </returns>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">An argument is not real.</exception>
    public ConversationHandlingEvent? TakeOverByHuman(
        UserId actor,
        DateTimeOffset takenOverAt,
        long afterSequence) =>
        ConversationHandlingEvent.RequireValid(actor, afterSequence) && TakeOverByHuman(takenOverAt)
            ? ConversationHandlingEvent.Record(
                Id,
                TenantId,
                ConversationHandlingEventKind.TakenOver,
                actor,
                takenOverAt,
                afterSequence)
            : null;

    /// <summary>
    /// Gives the conversation back to the AI, and says who did it and where.
    /// </summary>
    /// <returns>
    /// The event to store, or <see langword="null"/> when the AI was already
    /// answering it.
    /// </returns>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">An argument is not real.</exception>
    public ConversationHandlingEvent? ReturnToAi(
        UserId actor,
        DateTimeOffset returnedAt,
        long afterSequence) =>
        ConversationHandlingEvent.RequireValid(actor, afterSequence) && ReturnToAi(returnedAt)
            ? ConversationHandlingEvent.Record(
                Id,
                TenantId,
                ConversationHandlingEventKind.ReturnedToAi,
                actor,
                returnedAt,
                afterSequence)
            : null;

    /// <summary>
    /// Routes the conversation to one member, replacing any previous assignment.
    /// </summary>
    /// <remarks>
    /// The aggregate checks only that an assignment names somebody. That the
    /// somebody is a member of <em>this</em> tenant, and is allowed to reply, is
    /// not knowable from here — the conversation has no access to the workspace's
    /// membership or roles, and giving it one would drag the whole authorisation
    /// system into an aggregate that exists to own a transcript. Those are
    /// checked by the use case, and again by a composite foreign key in the
    /// database that makes a foreign-tenant assignment unrepresentable.
    /// </remarks>
    /// <returns><see langword="true"/> if this changed who it is assigned to.</returns>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">
    /// The member is empty, or the instant is not a real point in time.
    /// </exception>
    public bool AssignTo(UserId member, DateTimeOffset assignedAt)
    {
        RequireOpen("assign");

        if (member.IsEmpty)
        {
            throw new DomainValidationException("An assignment must name a member.")
            {
                Code = "conversations.assignment.invalid",
            };
        }

        DateTimeOffset instant = RequireInstant(assignedAt, "assignment instant");

        if (AssignedTo == member)
        {
            return false;
        }

        AssignedTo = member;
        RecordActivity(instant);

        return true;
    }

    /// <summary>
    /// Removes any assignment, leaving the conversation unclaimed. Idempotent.
    /// </summary>
    /// <remarks>
    /// Does not change <see cref="Handling"/>. A conversation a person took over
    /// and then released is still being handled by people; it just is not one
    /// particular person's any more.
    /// </remarks>
    /// <returns><see langword="true"/> if it had been assigned.</returns>
    /// <exception cref="DomainStateException">The conversation is closed.</exception>
    /// <exception cref="DomainValidationException">The instant is not a real point in time.</exception>
    public bool Unassign(DateTimeOffset unassignedAt)
    {
        RequireOpen("unassign");

        DateTimeOffset instant = RequireInstant(unassignedAt, "assignment instant");

        if (AssignedTo is null)
        {
            return false;
        }

        AssignedTo = null;
        RecordActivity(instant);

        return true;
    }

    private Message Append(
        MessageSender sender,
        MessageContentType contentType,
        string? text,
        DateTimeOffset sentAt)
    {
        DateTimeOffset instant = RequireInstant(sentAt, "sent instant");

        Message message = Message.Create(Id, sender, contentType, text, instant);

        // Appended only once the message has been built successfully, so a
        // rejected message leaves the conversation exactly as it was.
        // Appended at the end regardless of when it was sent: the history is in
        // ingestion order and is never reordered or sorted here.
        _messages.Add(message);
        RecordActivity(instant);

        return message;
    }

    private void RequireOpen(string operation)
    {
        if (Status != ConversationStatus.Open)
        {
            throw new DomainStateException(
                $"Cannot {operation} on a closed conversation. Reopen it first.")
            {
                Code = "conversations.closed",
            };
        }
    }

    /// <summary>
    /// Advances the activity high-water mark, never regressing it.
    /// </summary>
    /// <remarks>
    /// A message that was sent before the current mark is still accepted -- out
    /// of order delivery is normal in a distributed omnichannel system, and
    /// forcing channel adapters to buffer and reorder every message before
    /// calling the Domain would be a substantial correctness burden placed in
    /// exactly the wrong layer. The message keeps its own timestamp untouched;
    /// only this mark is left alone.
    /// </remarks>
    private void RecordActivity(DateTimeOffset instant)
    {
        if (instant > LastActivityAt)
        {
            LastActivityAt = instant;
        }
    }

    private static DateTimeOffset RequireInstant(DateTimeOffset instant, string what)
    {
        if (instant == default)
        {
            throw new DomainValidationException($"The {what} must be a real point in time.")
            {
                Code = "conversations.instant.invalid",
            };
        }

        // Stored in UTC: the instant is what matters, and a single representation
        // keeps comparison and storage unambiguous.
        return instant.ToUniversalTime();
    }

    private static void RequireDefined<TEnum>(TEnum value, string what)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new DomainValidationException(
                $"The supplied {what} ({Convert.ToInt32(value, CultureInfo.InvariantCulture)}) is not a recognised value.")
            {
                Code = "conversations.value.invalid",
            };
        }
    }
}
