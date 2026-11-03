using System.Globalization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WhatsApp;

/// <summary>The identity of one outbound delivery.</summary>
public readonly record struct WhatsAppDeliveryId
{
    private WhatsAppDeliveryId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static WhatsAppDeliveryId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    public static WhatsAppDeliveryId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("Delivery id must not be an empty GUID.")
                {
                    Code = "whatsapp.delivery.invalid",
                }
            : new WhatsAppDeliveryId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// Where a message stands with Meta.
/// </summary>
/// <remarks>
/// <strong>This is transport state, not conversation state.</strong> The message
/// exists in the transcript the moment it is stored; this says only what
/// happened when the platform tried to hand it to WhatsApp. A failed delivery
/// never removes a message, and a delivery receipt never adds one.
/// <para>
/// <strong><see cref="Unknown"/> is the honest state, and the important one.</strong>
/// When a send times out or the connection drops, the request may or may not have
/// reached Meta. Calling that "failed" and retrying would send the customer the
/// same message twice; calling it "sent" would claim something nobody knows. So
/// it is neither, and nothing retries it automatically.
/// </para>
/// </remarks>
public enum WhatsAppDeliveryStatus
{
    /// <summary>Stored, waiting for a worker.</summary>
    Pending = 1,

    /// <summary>A worker holds it and is about to call Meta.</summary>
    Sending = 2,

    /// <summary>Meta accepted it. Not the same as the customer receiving it.</summary>
    Sent = 3,

    /// <summary>Meta says it reached the customer's device.</summary>
    Delivered = 4,

    /// <summary>Meta says the customer opened it.</summary>
    Read = 5,

    /// <summary>Meta definitively refused it, or the platform refused to send.</summary>
    Failed = 6,

    /// <summary>
    /// The send was ambiguous: it may or may not have arrived, and nothing will
    /// resend it.
    /// </summary>
    Unknown = 7,
}

/// <summary>
/// One outbound WhatsApp message: the durable record of trying to deliver an
/// internal message to a customer.
/// </summary>
/// <remarks>
/// <strong>The internal message is the source of truth; this is the attempt.</strong>
/// An AI or human reply is stored in the transcript in its own transaction, and
/// a row here is created alongside it. A worker picks the row up, decrypts the
/// token, re-checks the service window and calls Meta — outside any database
/// transaction, because no transaction should span an HTTP call to somebody
/// else's datacentre.
/// <para>
/// <strong>One delivery per message.</strong> The database enforces it, which is
/// what stops two workers, or a worker and a retry, sending the same reply twice.
/// </para>
/// </remarks>
public sealed class WhatsAppDelivery
{
    /// <summary>The longest outbound text WhatsApp accepts.</summary>
    public const int MaxTextLength = 4096;

    /// <summary>The longest failure reason stored.</summary>
    public const int MaxReasonLength = 500;

    private WhatsAppDelivery(
        WhatsAppDeliveryId id,
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        ConversationId conversationId,
        MessageId messageId,
        WhatsAppCustomerId recipient,
        string text,
        DateTimeOffset createdAt)
    {
        Id = id;
        ConnectionId = connectionId;
        TenantId = tenantId;
        ConversationId = conversationId;
        MessageId = messageId;
        Recipient = recipient;
        Text = text;
        Status = WhatsAppDeliveryStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Attempts = 0;
    }

    /// <summary>Identity.</summary>
    public WhatsAppDeliveryId Id { get; }

    /// <summary>The connection to send from.</summary>
    public WhatsAppConnectionId ConnectionId { get; }

    /// <summary>The owning workspace.</summary>
    public TenantId TenantId { get; }

    /// <summary>The conversation this belongs to.</summary>
    public ConversationId ConversationId { get; }

    /// <summary>The internal message being delivered. Unique across deliveries.</summary>
    public MessageId MessageId { get; }

    /// <summary>Who it goes to. Personal data.</summary>
    public WhatsAppCustomerId Recipient { get; }

    /// <summary>
    /// The text to send.
    /// </summary>
    /// <remarks>
    /// Copied rather than read back from the message at send time. The worker
    /// runs later and possibly on another instance; carrying the text means a
    /// send needs one row rather than a join, and means an edited transcript
    /// could never change what was already queued for a customer.
    /// </remarks>
    public string Text { get; }

    /// <summary>Where it stands with Meta.</summary>
    public WhatsAppDeliveryStatus Status { get; private set; }

    /// <summary>
    /// Meta's identifier for the sent message, once there is one.
    /// </summary>
    /// <remarks>
    /// This is what later status webhooks are matched against. Null until Meta
    /// has accepted the message, and null for ever if it never did.
    /// </remarks>
    public ProviderMessageId? ProviderMessageId { get; private set; }

    /// <summary>
    /// Why it failed, in words safe to show a colleague.
    /// </summary>
    /// <remarks>
    /// Never the provider's raw response, which can contain the message text and
    /// request metadata. A short reason an administrator can act on.
    /// </remarks>
    public string? FailureReason { get; private set; }

    /// <summary>How many times a worker has taken it.</summary>
    public int Attempts { get; private set; }

    /// <summary>When it was queued, in UTC.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether a status webhook may still move this forward.</summary>
    public bool IsSettled =>
        Status is WhatsAppDeliveryStatus.Failed or WhatsAppDeliveryStatus.Unknown;

    /// <summary>Queues a message for delivery.</summary>
    public static WhatsAppDelivery Queue(
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        ConversationId conversationId,
        MessageId messageId,
        WhatsAppCustomerId recipient,
        string? text,
        DateTimeOffset createdAt)
    {
        string body = (text ?? string.Empty).Trim();

        if (body.Length == 0)
        {
            throw new DomainValidationException("An outbound message must carry text.")
            {
                Code = "whatsapp.message.required",
            };
        }

        if (body.Length > MaxTextLength)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"An outbound message must be {MaxTextLength} characters or fewer."))
            {
                Code = "whatsapp.message.too_long",
            };
        }

        if (messageId.IsEmpty)
        {
            throw new DomainValidationException("A delivery must name the message it carries.")
            {
                Code = "whatsapp.delivery.invalid",
            };
        }

        return new WhatsAppDelivery(
            WhatsAppDeliveryId.New(),
            connectionId,
            tenantId,
            conversationId,
            messageId,
            recipient,
            body,
            createdAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored delivery. Persistence only.</summary>
    public static WhatsAppDelivery FromStored(
        WhatsAppDeliveryId id,
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        ConversationId conversationId,
        MessageId messageId,
        WhatsAppCustomerId recipient,
        string text,
        WhatsAppDeliveryStatus status,
        ProviderMessageId? providerMessageId,
        string? failureReason,
        int attempts,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(id, connectionId, tenantId, conversationId, messageId, recipient, text, createdAt)
        {
            Status = status,
            ProviderMessageId = providerMessageId,
            FailureReason = failureReason,
            Attempts = attempts,
            UpdatedAt = updatedAt,
        };

    /// <summary>A worker has taken it and is about to call Meta.</summary>
    public void Claim(DateTimeOffset claimedAt)
    {
        Status = WhatsAppDeliveryStatus.Sending;
        Attempts++;

        Touch(claimedAt);
    }

    /// <summary>Meta accepted it.</summary>
    public void MarkSent(ProviderMessageId providerMessageId, DateTimeOffset sentAt)
    {
        ProviderMessageId = providerMessageId;
        Status = WhatsAppDeliveryStatus.Sent;
        FailureReason = null;

        Touch(sentAt);
    }

    /// <summary>Meta definitively refused it, or the platform refused to send.</summary>
    public void MarkFailed(string? reason, DateTimeOffset failedAt)
    {
        Status = WhatsAppDeliveryStatus.Failed;
        FailureReason = Shorten(reason);

        Touch(failedAt);
    }

    /// <summary>
    /// The send was ambiguous. Nothing will resend it.
    /// </summary>
    /// <remarks>
    /// A timeout, a dropped connection, a 5xx that may have been raised after
    /// the message was already queued on Meta's side. Retrying would risk
    /// sending a customer the same message twice, which is worse than a delivery
    /// an administrator has to look at.
    /// </remarks>
    public void MarkUnknown(string? reason, DateTimeOffset at)
    {
        Status = WhatsAppDeliveryStatus.Unknown;
        FailureReason = Shorten(reason);

        Touch(at);
    }

    /// <summary>
    /// Applies a status Meta reported, never regressing.
    /// </summary>
    /// <remarks>
    /// <strong>Status webhooks arrive out of order.</strong> A <c>read</c> can
    /// overtake a <c>delivered</c>, and Meta redelivers both. So progress is a
    /// ranking rather than an assignment: a later-ranked status advances, an
    /// earlier-ranked one is ignored, and the same one twice changes nothing.
    /// <para>
    /// A <c>failed</c> status always applies. Meta reporting a definitive failure
    /// after a <c>sent</c> is not a regression — it is the truth arriving late,
    /// and a colleague needs to see it.
    /// </para>
    /// </remarks>
    /// <returns><see langword="true"/> if this changed anything.</returns>
    public bool ApplyProviderStatus(
        WhatsAppDeliveryStatus reported,
        string? reason,
        DateTimeOffset at)
    {
        if (reported == WhatsAppDeliveryStatus.Failed)
        {
            if (Status == WhatsAppDeliveryStatus.Failed)
            {
                return false;
            }

            MarkFailed(reason, at);

            return true;
        }

        if (Rank(reported) <= Rank(Status))
        {
            return false;
        }

        Status = reported;

        Touch(at);

        return true;
    }

    /// <summary>
    /// How far along a status is, for comparing two of them.
    /// </summary>
    /// <remarks>
    /// Only the statuses a provider webhook can report are ranked. Anything else
    /// ranks below everything, so a stray value never overtakes real progress.
    /// </remarks>
    private static int Rank(WhatsAppDeliveryStatus status) =>
        status switch
        {
            WhatsAppDeliveryStatus.Sent => 1,
            WhatsAppDeliveryStatus.Delivered => 2,
            WhatsAppDeliveryStatus.Read => 3,
            _ => 0,
        };

    private static string? Shorten(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        string trimmed = reason.Trim();

        return trimmed.Length <= MaxReasonLength ? trimmed : trimmed[..MaxReasonLength];
    }

    private void Touch(DateTimeOffset instant)
    {
        if (instant == default)
        {
            throw new DomainValidationException("The instant must be a real point in time.")
            {
                Code = "whatsapp.instant.invalid",
            };
        }

        UpdatedAt = instant.ToUniversalTime();
    }
}
