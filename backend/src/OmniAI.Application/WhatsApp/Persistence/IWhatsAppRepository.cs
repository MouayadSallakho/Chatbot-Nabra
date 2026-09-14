using OmniAI.Application.Concurrency;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Application.WhatsApp.Persistence;

/// <summary>
/// A connection as an admin list renders one.
/// </summary>
/// <remarks>
/// Carries the agent's name because a list of numbers beside opaque agent ids
/// would be unreadable. It carries nothing about the access token but whether
/// one exists.
/// </remarks>
public sealed record WhatsAppConnectionRow(
    Guid Id,
    Guid AiAgentId,
    string? AiAgentName,
    string WabaId,
    string PhoneNumberId,
    string? DisplayPhoneNumber,
    WhatsAppConnectionStatus Status,
    bool HasCredential,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What an inbound webhook routes to.
/// </summary>
/// <remarks>
/// Deliberately tiny. The webhook edge needs to know which workspace and
/// connection a phone number id belongs to, and whether it is serving — nothing
/// else. Loading a whole aggregate on a path that runs for every message,
/// including every message this platform will ignore, would be work for nothing.
/// </remarks>
public sealed record WhatsAppRoute(
    WhatsAppConnectionId ConnectionId,
    TenantId TenantId,
    AiAgentId AiAgentId,
    bool IsServing);

/// <summary>
/// An inbound message a worker has claimed.
/// </summary>
/// <param name="PhoneNumberId">
/// The number it arrived on, read from the connection when the row was claimed.
/// Carried so the worker can re-check routing without a second lookup.
/// </param>
/// <param name="Attempts">
/// How many times this message has now been claimed, including this claim.
/// Carried so the worker can tell a message worth another attempt from one that
/// has run out, without asking the database a second question.
/// </param>
public sealed record ClaimedInboundMessage(
    InboundWhatsAppMessageId Id,
    WhatsAppConnectionId ConnectionId,
    TenantId TenantId,
    PhoneNumberId PhoneNumberId,
    ProviderMessageId ProviderMessageId,
    WhatsAppCustomerId CustomerId,
    string Text,
    DateTimeOffset SentAt,
    int Attempts);

/// <summary>
/// An outbound delivery a worker has claimed.
/// </summary>
public sealed record ClaimedDelivery(
    WhatsAppDeliveryId Id,
    WhatsAppConnectionId ConnectionId,
    TenantId TenantId,
    ConversationId ConversationId,
    PhoneNumberId PhoneNumberId,
    WhatsAppCustomerId Recipient,
    string Text);

/// <summary>
/// Reading and writing WhatsApp connections.
/// </summary>
public interface IWhatsAppConnectionRepository
{
    /// <summary>This workspace's connections, newest first.</summary>
    Task<IReadOnlyList<WhatsAppConnectionRow>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One connection of this workspace, with the version a change must present
    /// back.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> for a connection that does not exist and for one
    /// belonging to another workspace, indistinguishably.
    /// </returns>
    Task<Versioned<WhatsAppConnection>?> FindAsync(
        TenantId tenantId,
        WhatsAppConnectionId connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>The agent's name, for a list row.</summary>
    Task<string?> FindAgentNameAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default);

    /// <summary>Stores a new connection.</summary>
    /// <exception cref="WhatsAppPhoneNumberInUseException">
    /// Another connection anywhere on this deployment already claims that phone
    /// number id.
    /// </exception>
    Task<ConcurrencyToken> AddAsync(
        WhatsAppConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>Saves a changed connection, if it is still at this version.</summary>
    /// <exception cref="WhatsAppConnectionVersionMismatchException">
    /// Somebody else changed it in between.
    /// </exception>
    /// <exception cref="WhatsAppPhoneNumberInUseException">
    /// The new phone number id is already claimed elsewhere.
    /// </exception>
    Task<ConcurrencyToken> UpdateAsync(
        WhatsAppConnection connection,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the stored credential and records that one exists, together.
    /// </summary>
    /// <remarks>
    /// One transaction, because the ciphertext and the indicator are one fact.
    /// A connection that says it has a credential and has none would refuse
    /// every send with a confusing error; one that has a credential and denies
    /// it could never be activated.
    /// </remarks>
    Task<ConcurrencyToken> ReplaceCredentialAsync(
        WhatsAppConnection connection,
        byte[] protectedToken,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The protected access token for a connection, for the one use case that
    /// sends.
    /// </summary>
    /// <remarks>
    /// Its own read, reached only by the outbound worker. Keeping it off every
    /// other read path means a credential never travels through memory that has
    /// no use for it, and means the one call site is easy to find and review.
    /// </remarks>
    Task<byte[]?> FindProtectedCredentialAsync(
        WhatsAppConnectionId connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which connection a phone number id belongs to, if any is serving.
    /// </summary>
    /// <remarks>
    /// <strong>The only routing question a webhook is allowed to ask.</strong>
    /// The phone number id comes from Meta's own metadata, and nothing else in a
    /// payload may influence which workspace a message reaches.
    /// </remarks>
    Task<WhatsAppRoute?> FindRouteAsync(
        PhoneNumberId phoneNumberId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The durable inbound inbox.
/// </summary>
/// <remarks>
/// A webhook writes here and returns. A worker reads, processes and settles.
/// Nothing between the two is held in memory, so an instance restarting loses
/// no message.
/// </remarks>
public interface IWhatsAppInbox
{
    /// <summary>
    /// Records an inbound message, at most once per provider message id.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when this provider message id was already stored
    /// for this connection — a redelivery, which is a normal event and not an
    /// error.
    /// </returns>
    Task<bool> RecordAsync(
        InboundWhatsAppMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the next messages waiting, marking them claimed.
    /// </summary>
    /// <remarks>
    /// <strong>Must be safe with several instances running.</strong> Two workers
    /// asking at once must never receive the same row: the claim and the read
    /// are one atomic statement, not a select followed by an update.
    /// </remarks>
    Task<IReadOnlyList<ClaimedInboundMessage>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>It reached the conversation runtime.</summary>
    Task MarkProcessedAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Processing failed and will not be retried.</summary>
    Task MarkFailedAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives a claimed message back for another attempt.
    /// </summary>
    /// <remarks>
    /// For a failure that plausibly succeeds next time — a lock held by another
    /// turn, a momentary database problem. A worker that crashed leaves its rows
    /// claimed, and they become claimable again by age rather than by anybody
    /// having to notice.
    /// </remarks>
    Task ReleaseAsync(
        InboundWhatsAppMessageId id,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns rows a dead worker left claimed to the queue.
    /// </summary>
    /// <remarks>
    /// The whole of crash recovery. A claim is a timestamp, so a process that
    /// died mid-message leaves a row that simply looks old; nothing needs to
    /// detect the crash, and no lease has to be renewed.
    /// </remarks>
    Task<int> ReleaseStaleClaimsAsync(
        DateTimeOffset claimedBefore,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The durable outbound outbox.
/// </summary>
public interface IWhatsAppOutbox
{
    /// <summary>
    /// Queues a message for delivery, at most once per internal message.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when this message already has a delivery, which
    /// is what stops one reply being sent twice.
    /// </returns>
    Task<bool> QueueAsync(
        WhatsAppDelivery delivery,
        CancellationToken cancellationToken = default);

    /// <summary>Takes the next deliveries waiting, marking them sending.</summary>
    Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(
        int batchSize,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Meta accepted it.</summary>
    Task MarkSentAsync(
        WhatsAppDeliveryId id,
        ProviderMessageId providerMessageId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Meta refused it, or the platform refused to send.</summary>
    Task MarkFailedAsync(
        WhatsAppDeliveryId id,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>The send was ambiguous. Nothing will resend it.</summary>
    Task MarkUnknownAsync(
        WhatsAppDeliveryId id,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a status Meta reported, matched on the provider message id.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the delivery moved. False for an unknown
    /// message id, a duplicate event, or one that arrived out of order and would
    /// have regressed the state.
    /// </returns>
    Task<WhatsAppStatusOutcome> ApplyProviderStatusAsync(
        ProviderMessageId providerMessageId,
        WhatsAppDeliveryStatus reported,
        string? reason,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>Returns deliveries a dead worker left mid-send.</summary>
    /// <remarks>
    /// <strong>They are marked unknown, never re-queued.</strong> A worker that
    /// died after calling Meta may have had its message accepted; sending again
    /// would show the customer the same text twice. Somebody looks at it.
    /// </remarks>
    Task<int> AbandonStaleSendsAsync(
        DateTimeOffset claimedBefore,
        CancellationToken cancellationToken = default);

    /// <summary>The delivery state of messages in one conversation.</summary>
    /// <remarks>
    /// For the admin inbox, so a colleague can see whether the reply they sent
    /// reached the customer. Keyed by internal message id, because that is what
    /// a transcript is made of.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, WhatsAppDeliveryStatus>> ListStatusesAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}

/// <summary>What applying a provider status did.</summary>
/// <param name="Matched">Whether a delivery with that provider id exists.</param>
/// <param name="Changed">Whether it moved.</param>
/// <param name="TenantId">The workspace, when matched, so a change can be announced.</param>
/// <param name="ConversationId">The conversation, when matched.</param>
public sealed record WhatsAppStatusOutcome(
    bool Matched,
    bool Changed,
    TenantId TenantId,
    ConversationId ConversationId)
{
    /// <summary>Nothing on this deployment has that provider message id.</summary>
    public static WhatsAppStatusOutcome Unmatched { get; } =
        new(Matched: false, Changed: false, default, default);
}

/// <summary>
/// Mapping a WhatsApp customer to the conversation their messages join.
/// </summary>
public interface IWhatsAppContactStore
{
    /// <summary>
    /// The conversation this customer's next message joins, creating one if
    /// there is none or the current one has been closed.
    /// </summary>
    /// <remarks>
    /// <strong>Atomic, and safe against a race.</strong> Two webhooks from one
    /// person arriving together must not create two conversations: the unique
    /// index on the current mapping settles it and the loser reads the winner's
    /// row.
    /// </remarks>
    Task<ConversationId> ResolveConversationAsync(
        WhatsAppRoute route,
        WhatsAppCustomerId customerId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which customer a conversation belongs to, for sending a reply back.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> when the conversation did not come from WhatsApp.
    /// </returns>
    Task<WhatsAppConversationLink?> FindLinkAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}

/// <summary>Where a WhatsApp conversation came from, and who to answer.</summary>
public sealed record WhatsAppConversationLink(
    WhatsAppConnectionId ConnectionId,
    TenantId TenantId,
    WhatsAppCustomerId CustomerId,
    PhoneNumberId PhoneNumberId,
    bool IsServing);
