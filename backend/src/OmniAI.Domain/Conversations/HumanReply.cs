using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Conversations;

/// <summary>
/// The record of one reply a person sent: who sent it, which message it became,
/// and the key that makes sending it twice harmless.
/// </summary>
/// <remarks>
/// <strong>Why this is a record beside the message and not two columns on it.</strong>
/// It is the same shape as <see cref="ConversationTurn"/>, which is the platform's
/// existing answer to "this write must happen at most once": a small row, written
/// in the same transaction as the message, carrying a unique key the database
/// enforces. Doing the same thing the same way means there is one idea to learn
/// rather than two.
/// <para>
/// It also buys something two nullable columns on <c>messages</c> could not.
/// This row carries the tenant, so the database can hold a composite foreign key
/// on <c>(sender, tenant)</c> into the workspace's membership. A reply
/// attributed to somebody who does not work at that workspace is then not a bug
/// to be caught by a test — it is a row PostgreSQL refuses to store. Messages
/// carry no tenant of their own (their conversation does), so the same guarantee
/// is not available there.
/// </para>
/// <para>
/// <strong>The sender is never supplied by a browser.</strong> It is the
/// authenticated user the server resolved for the request. A client that sends
/// somebody else's id is sending a field nothing reads.
/// </para>
/// </remarks>
public sealed class HumanReply
{
    private HumanReply(
        HumanReplyId id,
        ConversationId conversationId,
        TenantId tenantId,
        UserId sender,
        ClientMessageId clientMessageId,
        MessageId messageId,
        DateTimeOffset sentAt)
    {
        Id = id;
        ConversationId = conversationId;
        TenantId = tenantId;
        Sender = sender;
        ClientMessageId = clientMessageId;
        MessageId = messageId;
        SentAt = sentAt;
    }

    /// <summary>Identity.</summary>
    public HumanReplyId Id { get; }

    /// <summary>The conversation replied to.</summary>
    public ConversationId ConversationId { get; }

    /// <summary>
    /// The workspace, carried so the database can check the sender belongs to it.
    /// </summary>
    public TenantId TenantId { get; }

    /// <summary>The authenticated user who sent it, as the server resolved them.</summary>
    public UserId Sender { get; }

    /// <summary>
    /// The key the composer generated before sending, and the reason a retry
    /// cannot double-post.
    /// </summary>
    public ClientMessageId ClientMessageId { get; }

    /// <summary>The message this reply became.</summary>
    public MessageId MessageId { get; }

    /// <summary>When it was sent, in UTC.</summary>
    public DateTimeOffset SentAt { get; }

    /// <summary>Records a reply a person has just sent.</summary>
    /// <exception cref="DomainValidationException">
    /// Any reference is empty, or the instant is not a real point in time.
    /// </exception>
    public static HumanReply Record(
        ConversationId conversationId,
        TenantId tenantId,
        UserId sender,
        ClientMessageId clientMessageId,
        MessageId messageId,
        DateTimeOffset sentAt)
    {
        if (conversationId.IsEmpty)
        {
            throw new DomainValidationException("A reply must belong to a conversation.");
        }

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A reply must belong to a workspace.");
        }

        if (sender.IsEmpty)
        {
            throw new DomainValidationException("A reply must name who sent it.");
        }

        if (messageId.IsEmpty)
        {
            throw new DomainValidationException("A reply must name its message.");
        }

        if (sentAt == default)
        {
            throw new DomainValidationException("The sent instant must be a real point in time.");
        }

        return new HumanReply(
            HumanReplyId.New(),
            conversationId,
            tenantId,
            sender,
            clientMessageId,
            messageId,
            sentAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored reply. Persistence only.</summary>
    public static HumanReply FromStored(
        HumanReplyId id,
        ConversationId conversationId,
        TenantId tenantId,
        UserId sender,
        ClientMessageId clientMessageId,
        MessageId messageId,
        DateTimeOffset sentAt) =>
        new(id, conversationId, tenantId, sender, clientMessageId, messageId, sentAt);
}
