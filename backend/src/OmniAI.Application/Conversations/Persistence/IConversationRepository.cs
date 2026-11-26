using OmniAI.Application.Concurrency;
using OmniAI.Application.Customers;
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
    ConversationMessageAuthor? SentBy = null,
    string[]? Attachments = null);

/// <summary>
/// The person behind a human reply, for the workspace's own screens.
/// </summary>
/// <remarks>
/// Carries the email because an administrator looking at their own team needs to
/// know who said something, and inside the workspace that is not a disclosure.
/// It leaves through the admin API and no other: the public transports drop it
/// before a view is built, and a test asserts they do.
/// <para>
/// Phase 39.1 adds the person's display name beside the email, read in the same
/// query; null when they have not given one. The email stays.
/// </para>
/// </remarks>
public sealed record ConversationMessageAuthor(Guid UserId, string Email, string? DisplayName = null);

/// <summary>Inbox filters intersect the current view. The upper activity bound is exclusive.</summary>
public sealed record InboxFilters(
    ChannelKind? Channel = null,
    ConversationStatus? Status = null,
    Guid? AssignedTo = null,
    bool Unassigned = false,
    DateTimeOffset? ActiveFrom = null,
    DateTimeOffset? ActiveBefore = null);

/// <summary>One page of a conversation's history.</summary>
/// <param name="Items">Messages in ingestion order, oldest first.</param>
/// <param name="NextCursor">
/// The ingestion sequence to continue after, or <see langword="null"/> when the
/// page reached the end of the conversation.
/// </param>
/// <param name="PreviousCursor">
/// The ingestion sequence to read the page before this one with (as
/// <c>before</c>), or <see langword="null"/> when nothing is earlier. Filled by
/// the backward reads and by the admin handler; the ascending repository read
/// leaves it null, so the public transports that share it are unchanged.
/// </param>
public sealed record ConversationMessagePage(
    IReadOnlyList<ConversationMessageRecord> Items,
    long? NextCursor,
    long? PreviousCursor = null);

/// <summary>Who a conversation is with, as an inbox row shows them.</summary>
/// <param name="Id">The customer's id (the canonical record: a merge moves conversations).</param>
/// <param name="DisplayName">The label somebody in the workspace gave them, if any.</param>
/// <param name="LabelCode">Which label to show when there is no name; see <c>CustomerLabels</c>.</param>
/// <param name="Reference">
/// For a WhatsApp conversation, the number it runs on, already masked to its last
/// four digits. <see langword="null"/> on every other channel. There is no raw form.
/// </param>
/// <param name="ShortCode">Six characters that tell unnamed customers apart; never a key.</param>
public sealed record ConversationCustomer(
    Guid Id,
    string? DisplayName,
    CustomerLabelCode LabelCode,
    string? Reference,
    string ShortCode);

/// <summary>One conversation, as the admin list publishes it.</summary>
/// <remarks>
/// A summary rather than the aggregate: a list of a thousand conversations must
/// not load a thousand transcripts, so the row carries a count and a short
/// preview and the transcript is fetched only when one is opened.
/// </remarks>
/// <param name="Customer">Who it is with (Phase 37.4).</param>
/// <param name="Unread">
/// Whether its newest message is beyond the reader's own read marker, or the
/// reader has no marker and it has a message (Phase 37.4).
/// </param>
/// <param name="TakenOverBy">
/// Who took it over (Phase 38.7a): the actor of the conversation's newest handling
/// event (by transcript position, then time, then id) when that event is a
/// takeover and a person handles the conversation now. <see langword="null"/>
/// while the AI handles it, and for a takeover from before Phase 37.4, which
/// recorded no event. Named exactly as the transcript names event actors,
/// including somebody who has since left the workspace.
/// </param>
/// <param name="NeedsPersonReason">
/// Why it waits on a person, or <see langword="null"/> when it does not (Phase 38.7a).
/// </param>
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
    ConversationMessageAuthor? AssignedTo,
    ConversationCustomer? Customer = null,
    bool Unread = false,
    ConversationMessageAuthor? TakenOverBy = null,
    NeedsPersonReason? NeedsPersonReason = null)
{
    /// <summary>
    /// Whether it is in <c>filter=needs-person</c>: exactly when it has a reason.
    /// </summary>
    public bool NeedsPerson => NeedsPersonReason is not null;
}

/// <summary>
/// Why a conversation waits on a person (Phase 38.7a, BA-7): the three branches of
/// the predicate behind <c>filter=needs-person</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every reason needs the conversation open and its newest message the
/// customer's. When two branches hold, <see cref="PersonHandling"/> wins: a person
/// handles it now, so their reply is what is awaited, whatever happened to the
/// AI's turn before they took it over. <see cref="AiTurnFailed"/> and
/// <see cref="ReturnedUnanswered"/> can never hold together: one turn is either
/// failed or completed.
/// </para>
/// <para>
/// The numbers are what the database statement computes; 0 means none.
/// </para>
/// </remarks>
public enum NeedsPersonReason
{
    /// <summary>A person handles it, and the customer wrote last.</summary>
    PersonHandling = 1,

    /// <summary>The AI's turn for the customer's last message failed.</summary>
    AiTurnFailed = 2,

    /// <summary>
    /// It was handed back to the AI before anybody answered the customer's last
    /// message, which the AI never answers retroactively.
    /// </summary>
    ReturnedUnanswered = 3,
}

/// <summary>Which conversations a list shows.</summary>
public enum ConversationListFilter
{
    /// <summary>Every conversation of the workspace.</summary>
    All = 0,

    /// <summary>
    /// Open, the customer wrote last, and one of the <see cref="NeedsPersonReason"/>
    /// branches holds (plan D-5, widened in 37.4 Revision 1).
    /// </summary>
    NeedsPerson = 1,

    /// <summary>
    /// Assigned to the reader, or taken over by the reader (Phase 39.1, BA39-5):
    /// the inbox's "Mine" tab.
    /// </summary>
    Mine = 2,
}

/// <summary>What changed in a transcript besides its messages.</summary>
public enum TranscriptEventKind
{
    /// <summary>The AI did not answer a customer message (its turn failed).</summary>
    AiTurnFailed = 1,

    /// <summary>A person took the conversation over.</summary>
    TakenOver = 2,

    /// <summary>A person handed the conversation back to the AI.</summary>
    ReturnedToAi = 3,
}

/// <summary>
/// Something that happened in a conversation that is not a message, placed by
/// the message it follows.
/// </summary>
/// <param name="Id">
/// Stable: the failed turn's id, or the handling event's id. An event on a page
/// boundary can be served with both pages; the id is how a client keeps one.
/// </param>
/// <param name="AfterSequence">
/// The ingestion sequence of the message it follows (zero before the first).
/// </param>
/// <param name="Actor">Who, for a takeover or a return; <see langword="null"/> for a failed turn.</param>
/// <param name="Reason">
/// For a failed turn, a stable code with no provider detail; otherwise
/// <see langword="null"/>.
/// </param>
public sealed record TranscriptEvent(
    Guid Id,
    TranscriptEventKind Kind,
    DateTimeOffset At,
    long AfterSequence,
    ConversationMessageAuthor? Actor,
    string? Reason);

/// <summary>A page of conversations.</summary>
public sealed record ConversationPage(
    IReadOnlyList<ConversationSummary> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>One conversation's detail, for the admin.</summary>
/// <param name="Customer">Who it is with (Phase 37.4).</param>
/// <param name="Unread">As on <see cref="ConversationSummary"/>, for the reader (Phase 37.4).</param>
/// <param name="LastReadSequence">
/// The reader's own read marker, or <see langword="null"/> when they have never
/// marked this conversation read (Phase 37.4).
/// </param>
/// <param name="TakenOverBy">As on <see cref="ConversationSummary"/> (Phase 38.7a).</param>
/// <param name="NeedsPersonReason">As on <see cref="ConversationSummary"/> (Phase 38.7a).</param>
public sealed record ConversationDetail(
    Guid Id,
    ChannelKind Channel,
    ConversationStatus Status,
    ConversationHandling Handling,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    string? AiAgentName,
    int MessageCount,
    ConversationMessageAuthor? AssignedTo,
    ConversationCustomer? Customer = null,
    bool Unread = false,
    long? LastReadSequence = null,
    ConversationMessageAuthor? TakenOverBy = null,
    NeedsPersonReason? NeedsPersonReason = null)
{
    public OmniAI.Application.WhatsApp.ReplyWindow? ReplyWindow { get; init; }

    /// <summary>As on <see cref="ConversationSummary"/>.</summary>
    public bool NeedsPerson => NeedsPersonReason is not null;
}

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
    Task<DateTimeOffset?> LastCustomerMessageAtAsync(ConversationId conversationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationMessageAuthor>> ListAssignedMembersAsync(
        TenantId tenantId, CancellationToken cancellationToken);
    /// <summary>One page of a workspace's conversations, most recently active first.</summary>
    /// <param name="filter">Which conversations; the total counts the filtered set.</param>
    /// <param name="viewer">Whose read markers decide <c>Unread</c>.</param>
    Task<ConversationPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        ConversationListFilter filter,
        UserId viewer,
        string? search = null,
        InboxFilters? criteria = null,
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
    /// <param name="viewer">Whose read marker decides <c>Unread</c> and <c>LastReadSequence</c>.</param>
    Task<Versioned<ConversationDetail>?> FindAsync(
        TenantId tenantId,
        ConversationId conversationId,
        UserId viewer,
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
    /// A page of history read from the newest end: the <paramref name="limit"/>
    /// messages before a cursor, or the newest ones. Still in ingestion order
    /// inside the page, so a client merging pages by sequence needs no change.
    /// </summary>
    /// <param name="beforeSequence">
    /// Only messages with a lower sequence, or <see langword="null"/> for the
    /// newest messages of the conversation.
    /// </param>
    /// <returns>
    /// The page, with <c>PreviousCursor</c> set when earlier messages exist and
    /// <c>NextCursor</c> set when later ones do.
    /// </returns>
    Task<ConversationMessagePage> ListMessagesBeforeAsync(
        ConversationId conversationId,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Whether this conversation has a message with a lower sequence.</summary>
    Task<bool> HasMessagesBeforeAsync(
        ConversationId conversationId,
        long sequence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What happened in a span of the transcript besides its messages: failed AI
    /// turns, takeovers and returns to the AI, ordered by where they sit.
    /// </summary>
    /// <param name="fromSequence">Inclusive lower bound on <c>AfterSequence</c>.</param>
    /// <param name="toSequence">Inclusive upper bound on <c>AfterSequence</c>.</param>
    /// <remarks>
    /// For the workspace's own transcript only. No public transport calls this,
    /// and none of their views has a field an event could be put into.
    /// </remarks>
    Task<IReadOnlyList<TranscriptEvent>> ListTranscriptEventsAsync(
        ConversationId conversationId,
        long fromSequence,
        long toSequence,
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
