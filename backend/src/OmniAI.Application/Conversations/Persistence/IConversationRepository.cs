using OmniAI.Application.Concurrency;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Conversations.Persistence;

/// <summary>
/// One message, as every read path publishes it.
/// </summary>
/// <param name="Id">The message's identity.</param>
/// <param name="Sender">Who wrote it.</param>
/// <param name="Text">What was written.</param>
/// <param name="SentAt">When it was sent — display only, never an ordering.</param>
/// <param name="IngestionSequence">
/// The platform's ordering. Monotonic across the whole deployment and therefore
/// sparse within one conversation; only its relative order means anything. This
/// is also the pagination cursor, because it is the only value that is both
/// stable and strictly increasing in the order history is read.
/// </param>
/// <param name="SentBy">
/// For a message a person sent, who sent it. <see langword="null"/> for every
/// other sender, and never read by a public transport: a visitor is told the
/// business replied, not which employee typed it.
/// </param>
public sealed record ConversationMessageRecord(
    Guid Id,
    MessageSender Sender,
    string Text,
    DateTimeOffset SentAt,
    long IngestionSequence,
    ConversationMessageAuthor? SentBy = null);

/// <summary>
/// The person behind a human reply, for the workspace's own screens.
/// </summary>
/// <remarks>
/// Carries the email because an administrator looking at their own team needs to
/// know who said something, and inside the workspace that is not a disclosure.
/// It leaves through the admin API and no other: the public transports drop it
/// before a view is built, and a test asserts they do.
/// </remarks>
public sealed record ConversationMessageAuthor(Guid UserId, string Email);

/// <summary>One page of a conversation's history.</summary>
/// <param name="Items">Messages in ingestion order, oldest first.</param>
/// <param name="NextCursor">
/// The ingestion sequence to continue after, or <see langword="null"/> when the
/// page reached the end of the conversation.
/// </param>
public sealed record ConversationMessagePage(
    IReadOnlyList<ConversationMessageRecord> Items,
    long? NextCursor);

/// <summary>One conversation, as the admin list publishes it.</summary>
/// <remarks>
/// A summary rather than the aggregate: a list of a thousand conversations must
/// not load a thousand transcripts, so the row carries a count and a short
/// preview and the transcript is fetched only when one is opened.
/// </remarks>
public sealed record ConversationSummary(
    Guid Id,
    ChannelKind Channel,
    ConversationStatus Status,
    ConversationHandling Handling,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    string? AiAgentName,
    int MessageCount,
    string? LastMessagePreview,
    MessageSender? LastMessageSender,
    ConversationMessageAuthor? AssignedTo);

/// <summary>A page of conversations.</summary>
public sealed record ConversationPage(
    IReadOnlyList<ConversationSummary> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>One conversation's detail, for the admin.</summary>
public sealed record ConversationDetail(
    Guid Id,
    ChannelKind Channel,
    ConversationStatus Status,
    ConversationHandling Handling,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    string? AiAgentName,
    int MessageCount,
    ConversationMessageAuthor? AssignedTo);

/// <summary>
/// Reading conversations, for administrators and for the visitors who own them.
/// </summary>
/// <remarks>
/// <strong>Every operation is scoped by whatever the caller was authorised
/// on.</strong> The admin reads name a tenant and the predicate carries it, so a
/// conversation id belonging to another workspace answers not-found rather than
/// somebody else's transcript. The message reads are scoped by conversation
/// alone, because both callers — an administrator who has already been authorised
/// on the tenant, and a visitor who has already been authorised on that exact
/// conversation — have had their access settled before they get here.
/// <para>
/// <strong>Read-only, deliberately.</strong> The runtime's writes live behind
/// their own contract, because appending a message and answering it is a
/// transactional sequence rather than a save.
/// </para>
/// </remarks>
public interface IConversationRepository
{
    /// <summary>One page of a workspace's conversations, most recently active first.</summary>
    Task<ConversationPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One conversation of this workspace, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Null covers both "no such conversation" and "that conversation belongs to
    /// another workspace", deliberately indistinguishably.
    /// </remarks>
    /// <remarks>
    /// Versioned, because the admin screens do not only read a conversation any
    /// more: taking one over and routing it are conditional writes, and the
    /// version the reader saw is what they must present back.
    /// </remarks>
    Task<Versioned<ConversationDetail>?> FindAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this conversation belongs to this workspace.
    /// </summary>
    /// <remarks>
    /// Asked before a message page is read, so the cheap ownership question is
    /// answered without loading any transcript.
    /// </remarks>
    Task<bool> BelongsToAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of history, in ingestion order, starting after a cursor.
    /// </summary>
    /// <param name="afterSequence">
    /// The cursor from a previous page, or <see langword="null"/> to start at the
    /// beginning of the conversation.
    /// </param>
    Task<ConversationMessagePage> ListMessagesAsync(
        ConversationId conversationId,
        long? afterSequence,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent messages, in ingestion order, for assembling AI context.
    /// </summary>
    /// <remarks>
    /// Bounded at the database rather than by reading everything and discarding:
    /// a conversation's history has no upper size, and the context policy needs
    /// only its tail. The rows come back oldest-first so the policy never has to
    /// reorder anything.
    /// </remarks>
    Task<IReadOnlyList<ConversationMessageRecord>> ListRecentMessagesAsync(
        ConversationId conversationId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>The agent bound to a conversation through its web chat session, if any.</summary>
    Task<AiAgentId?> FindBoundAgentAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}
