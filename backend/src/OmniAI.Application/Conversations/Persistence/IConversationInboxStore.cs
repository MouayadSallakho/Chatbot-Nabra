using OmniAI.Application.Concurrency;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Conversations.Persistence;

/// <summary>
/// What an appended human reply turned out to be.
/// </summary>
/// <param name="Message">The stored message, whether it was written now or before.</param>
/// <param name="WasAlreadyStored">
/// <see langword="true"/> when this exact client message id had already produced
/// a reply and this call stored nothing. The caller returns the same result
/// either way; it matters only for deciding whether anybody needs telling.
/// </param>
public sealed record HumanReplyOutcome(ConversationMessageRecord Message, bool WasAlreadyStored);

/// <summary>
/// The writes an operational inbox makes to a conversation.
/// </summary>
/// <remarks>
/// <strong>Separate from <see cref="IConversationRepository"/> because reading and
/// writing a conversation are different jobs.</strong> The read contract is
/// deliberately read-only and is used by the public transports too; this one is
/// reachable only from admin use cases that have already been authorised on the
/// workspace.
/// <para>
/// <strong>Nothing here takes a lock.</strong> Serialisation is the use case's
/// decision, because a lock has to span more than a save: taking a conversation
/// over has to exclude a generation that is already running, and a save that
/// locked for its own duration would not exclude anything.
/// </para>
/// </remarks>
public interface IConversationInboxStore
{
    /// <summary>
    /// Loads a conversation of this workspace for modification, with its version.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> for a conversation that does not exist and for one
    /// belonging to another workspace, indistinguishably.
    /// </returns>
    Task<Versioned<Conversation>?> FindForUpdateAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a conversation whose handling or assignment has changed, if it is
    /// still at the version the caller read.
    /// </summary>
    /// <exception cref="ConversationVersionMismatchException">
    /// Somebody else changed it in between.
    /// </exception>
    Task<ConcurrencyToken> SaveAsync(
        Conversation conversation,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a reply from a person, at most once per client message id.
    /// </summary>
    /// <remarks>
    /// The message and the reply record are written in one save, so a stored
    /// message always has an author and a stored author always has a message.
    /// A duplicate key is not an error: the stored reply is read back and
    /// returned, because the caller asked for that reply to exist and it does.
    /// </remarks>
    Task<HumanReplyOutcome> AppendReplyAsync(
        Conversation conversation,
        UserId sender,
        ClientMessageId clientMessageId,
        string text,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this workspace still has a member with this user id.
    /// </summary>
    Task<bool> IsMemberAsync(
        TenantId tenantId,
        UserId member,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The people this conversation may be routed to: members of this workspace
    /// who currently hold <c>conversations.reply</c>.
    /// </summary>
    /// <remarks>
    /// Computed from roles at the moment it is asked, never stored. A member who
    /// loses the permission stops being offered without anything having to notice.
    /// </remarks>
    Task<IReadOnlyList<ConversationMessageAuthor>> ListEligibleAssigneesAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);
}
