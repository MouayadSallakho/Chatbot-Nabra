using System.Globalization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WhatsApp;

/// <summary>The identity of one inbound record.</summary>
public readonly record struct InboundWhatsAppMessageId
{
    private InboundWhatsAppMessageId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static InboundWhatsAppMessageId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    public static InboundWhatsAppMessageId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("Inbound message id must not be an empty GUID.")
                {
                    Code = "whatsapp.inbound.invalid",
                }
            : new InboundWhatsAppMessageId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// How far an inbound message has got.
/// </summary>
/// <remarks>
/// Four states, each of which something actually does. There is no
/// <c>Received</c> beside <c>Pending</c>, because nothing would ever distinguish
/// them.
/// </remarks>
public enum InboundWhatsAppStatus
{
    /// <summary>Stored, waiting for a worker.</summary>
    Pending = 1,

    /// <summary>A worker holds it.</summary>
    Claimed = 2,

    /// <summary>It reached the conversation runtime.</summary>
    Processed = 3,

    /// <summary>
    /// Deliberately not processed: a message type this phase does not accept.
    /// </summary>
    /// <remarks>
    /// Stored rather than dropped, so Meta redelivering it finds the record and
    /// does nothing a second time. It produces no customer message, no AI
    /// attempt and nothing in anybody's transcript.
    /// </remarks>
    Ignored = 4,

    /// <summary>Processing failed in a way a retry will not fix.</summary>
    Failed = 5,
}

/// <summary>
/// One WhatsApp message, stored the moment it arrives and processed afterwards.
/// </summary>
/// <remarks>
/// <strong>The whole point is to answer Meta quickly and truthfully.</strong>
/// Meta retries a webhook it does not get a prompt success from, and running a
/// retrieval and a provider call before replying would guarantee timeouts,
/// duplicate deliveries and a customer message written several times. So the
/// webhook verifies the signature, routes by phone number id, writes this row,
/// and returns. Everything expensive happens afterwards, from a worker.
/// <para>
/// <strong>Normalised, not the raw payload.</strong> Only the fields this
/// platform acts on are kept. Storing the whole webhook body would mean keeping
/// Meta's profile names, pricing metadata and whatever it adds next, indefinitely
/// and for no purpose — and it would put personal data in a place nothing
/// governs.
/// </para>
/// <para>
/// <strong>The provider message id is the idempotency key</strong>, unique per
/// connection. Meta redelivers; the index is what makes a redelivery a no-op
/// rather than a second conversation turn.
/// </para>
/// </remarks>
public sealed class InboundWhatsAppMessage
{
    /// <summary>The longest inbound text this platform stores.</summary>
    /// <remarks>
    /// WhatsApp's own limit for a text body is 4096 characters. Matching it means
    /// a legitimate message is never truncated and an over-long one is refused at
    /// the edge rather than silently cut in half.
    /// </remarks>
    public const int MaxTextLength = 4096;

    private InboundWhatsAppMessage(
        InboundWhatsAppMessageId id,
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        ProviderMessageId providerMessageId,
        WhatsAppCustomerId customerId,
        string? text,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt,
        InboundWhatsAppStatus status)
    {
        Id = id;
        ConnectionId = connectionId;
        TenantId = tenantId;
        ProviderMessageId = providerMessageId;
        CustomerId = customerId;
        Text = text;
        SentAt = sentAt;
        ReceivedAt = receivedAt;
        Status = status;
        Attempts = 0;
    }

    /// <summary>Identity.</summary>
    public InboundWhatsAppMessageId Id { get; }

    /// <summary>The connection it arrived on.</summary>
    public WhatsAppConnectionId ConnectionId { get; }

    /// <summary>The owning workspace, from the connection and nowhere else.</summary>
    public TenantId TenantId { get; }

    /// <summary>Meta's identifier, and this row's idempotency key.</summary>
    public ProviderMessageId ProviderMessageId { get; }

    /// <summary>Who wrote. Personal data.</summary>
    public WhatsAppCustomerId CustomerId { get; }

    /// <summary>
    /// What they wrote, or <see langword="null"/> for an ignored type.
    /// </summary>
    /// <remarks>
    /// Null rather than a placeholder. An ignored message has no text, and
    /// writing <c>"[image]"</c> here would put a sentence the customer never
    /// said into a transcript and hand it to an AI as though they had.
    /// </remarks>
    public string? Text { get; }

    /// <summary>When the customer sent it, as Meta reports.</summary>
    public DateTimeOffset SentAt { get; }

    /// <summary>When this platform received the webhook.</summary>
    public DateTimeOffset ReceivedAt { get; }

    /// <summary>How far it has got.</summary>
    public InboundWhatsAppStatus Status { get; private set; }

    /// <summary>How many times a worker has taken it.</summary>
    /// <remarks>
    /// Bounded by the worker, so a message that fails the same way for ever
    /// stops being retried instead of occupying a worker until somebody notices.
    /// </remarks>
    public int Attempts { get; private set; }

    /// <summary>When it was last touched, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records a text message for processing.</summary>
    public static InboundWhatsAppMessage ForText(
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        ProviderMessageId providerMessageId,
        WhatsAppCustomerId customerId,
        string? text,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt)
    {
        string body = (text ?? string.Empty).Trim();

        if (body.Length == 0)
        {
            throw new DomainValidationException("An inbound text message must carry text.")
            {
                Code = "whatsapp.inbound.invalid",
            };
        }

        if (body.Length > MaxTextLength)
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"An inbound text message must be {MaxTextLength} characters or fewer."))
            {
                Code = "whatsapp.inbound.invalid",
            };
        }

        return new InboundWhatsAppMessage(
            InboundWhatsAppMessageId.New(),
            connectionId,
            tenantId,
            providerMessageId,
            customerId,
            body,
            sentAt.ToUniversalTime(),
            receivedAt.ToUniversalTime(),
            InboundWhatsAppStatus.Pending)
        {
            UpdatedAt = receivedAt.ToUniversalTime(),
        };
    }

    /// <summary>
    /// Records a message this phase does not act on.
    /// </summary>
    /// <remarks>
    /// An image, a location, a reaction. It is written so a redelivery is
    /// recognised, and it is born <see cref="InboundWhatsAppStatus.Ignored"/> so
    /// no worker ever picks it up.
    /// </remarks>
    public static InboundWhatsAppMessage Ignored(
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        ProviderMessageId providerMessageId,
        WhatsAppCustomerId customerId,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt) =>
        new(
            InboundWhatsAppMessageId.New(),
            connectionId,
            tenantId,
            providerMessageId,
            customerId,
            text: null,
            sentAt.ToUniversalTime(),
            receivedAt.ToUniversalTime(),
            InboundWhatsAppStatus.Ignored)
        {
            UpdatedAt = receivedAt.ToUniversalTime(),
        };

    /// <summary>Rehydrates a stored record. Persistence only.</summary>
    public static InboundWhatsAppMessage FromStored(
        InboundWhatsAppMessageId id,
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        ProviderMessageId providerMessageId,
        WhatsAppCustomerId customerId,
        string? text,
        DateTimeOffset sentAt,
        DateTimeOffset receivedAt,
        InboundWhatsAppStatus status,
        int attempts,
        DateTimeOffset updatedAt) =>
        new(
            id,
            connectionId,
            tenantId,
            providerMessageId,
            customerId,
            text,
            sentAt,
            receivedAt,
            status)
        {
            Attempts = attempts,
            UpdatedAt = updatedAt,
        };

    /// <summary>A worker has taken it.</summary>
    public void Claim(DateTimeOffset claimedAt)
    {
        Status = InboundWhatsAppStatus.Claimed;
        Attempts++;

        UpdatedAt = claimedAt.ToUniversalTime();
    }

    /// <summary>It reached the conversation runtime.</summary>
    public void MarkProcessed(DateTimeOffset processedAt)
    {
        Status = InboundWhatsAppStatus.Processed;

        UpdatedAt = processedAt.ToUniversalTime();
    }

    /// <summary>Processing failed and will not be retried.</summary>
    public void MarkFailed(DateTimeOffset failedAt)
    {
        Status = InboundWhatsAppStatus.Failed;

        UpdatedAt = failedAt.ToUniversalTime();
    }

    /// <summary>Give it back for another attempt.</summary>
    public void Release(DateTimeOffset releasedAt)
    {
        Status = InboundWhatsAppStatus.Pending;

        UpdatedAt = releasedAt.ToUniversalTime();
    }
}
