using System.Collections.ObjectModel;
using System.Globalization;
using OmniAI.Domain.Tenants;

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
        ChannelKind channel,
        DateTimeOffset startedAt)
    {
        Id = id;
        TenantId = tenantId;
        Channel = channel;
        Status = ConversationStatus.Open;
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

    /// <summary>The channel this conversation takes place on. Fixed at creation.</summary>
    public ChannelKind Channel { get; }

    /// <summary>Current lifecycle state.</summary>
    public ConversationStatus Status { get; private set; }

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
    /// <param name="channel">The channel it takes place on.</param>
    /// <param name="startedAt">The effective start instant, supplied by the caller.</param>
    /// <exception cref="DomainValidationException">
    /// The tenant id is empty, the channel is not a recognised value, or the
    /// instant is not a real point in time.
    /// </exception>
    public static Conversation Start(TenantId tenantId, ChannelKind channel, DateTimeOffset startedAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A conversation must belong to a tenant.");
        }

        RequireDefined(channel, "channel");

        return new Conversation(
            ConversationId.New(),
            tenantId,
            channel,
            RequireInstant(startedAt, "start instant"));
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
                "so that its sender and content type stay consistent.");
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
                $"Cannot {operation} on a closed conversation. Reopen it first.");
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
            throw new DomainValidationException($"The {what} must be a real point in time.");
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
                $"The supplied {what} ({Convert.ToInt32(value, CultureInfo.InvariantCulture)}) is not a recognised value.");
        }
    }
}
