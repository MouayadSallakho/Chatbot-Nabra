namespace OmniAI.Domain.Billing;

/// <summary>What the platform does with a stored provider event.</summary>
public enum BillingEventStatus
{
    /// <summary>Stored and waiting for a worker.</summary>
    Pending = 1,

    /// <summary>A worker is holding it.</summary>
    Claimed = 2,

    /// <summary>Applied. Nothing more to do.</summary>
    Processed = 3,

    /// <summary>
    /// Nothing can be done with it, and retrying would not change that.
    /// </summary>
    /// <remarks>
    /// An event about a subscription this platform does not know, or a price it
    /// cannot map to an offer. Recorded rather than retried, because a hot loop
    /// over an unanswerable event hides real failures behind noise.
    /// </remarks>
    Unprocessable = 4,
}

/// <summary>
/// A verified provider event, stored before it is acted on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The endpoint's whole job is to verify, store and acknowledge.</strong>
/// Stripe expects a prompt 2xx and redelivers anything slow; fetching a
/// subscription, taking a workspace lock and reassigning a plan is not work that
/// belongs inside a webhook request. So the request persists this and returns,
/// and a worker does the rest.
/// </para>
/// <para>
/// <strong>Normalised fields only — never the raw payload.</strong> A provider
/// event body carries customer names, addresses, card brands, last four digits
/// and invoice lines, none of which this platform needs and all of which it would
/// then be responsible for. What is kept is what identifies the event and what it
/// is about.
/// </para>
/// <para>
/// <strong>The provider's event id is unique per provider</strong>, enforced by
/// the database. A redelivery finds the row already there and produces one stored
/// event and one transition, whether it arrives a second later or concurrently.
/// </para>
/// </remarks>
public sealed class BillingEvent
{
    private BillingEvent(
        BillingEventId id,
        BillingProvider provider,
        ProviderReference providerEventId,
        string eventType,
        DateTimeOffset providerCreatedAt,
        DateTimeOffset receivedAt)
    {
        Id = id;
        Provider = provider;
        ProviderEventId = providerEventId;
        EventType = eventType;
        ProviderCreatedAt = providerCreatedAt;
        ReceivedAt = receivedAt;
        Status = BillingEventStatus.Pending;
    }

    public BillingEventId Id { get; }

    public BillingProvider Provider { get; }

    /// <summary>The provider's own event id. Unique per provider.</summary>
    public ProviderReference ProviderEventId { get; }

    /// <summary>The provider's event type, as a plain string.</summary>
    /// <remarks>
    /// A string rather than an enum: providers add event types, and an unknown
    /// one must be storable and ignorable rather than a deserialisation failure
    /// at the edge.
    /// </remarks>
    public string EventType { get; }

    /// <summary>The subscription the event concerns, where it names one.</summary>
    public ProviderReference? ProviderSubscriptionId { get; private set; }

    /// <summary>The customer the event concerns, where it names one.</summary>
    public ProviderReference? ProviderCustomerId { get; private set; }

    /// <summary>The checkout session the event concerns, where it names one.</summary>
    public ProviderReference? ProviderSessionId { get; private set; }

    public DateTimeOffset ProviderCreatedAt { get; }

    public DateTimeOffset ReceivedAt { get; }

    public BillingEventStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>
    /// Why this event could not be applied, in the platform's own words.
    /// </summary>
    /// <remarks>
    /// Short, operational and safe to read: "no offer maps this price", not the
    /// provider's payload.
    /// </remarks>
    public string? Failure { get; private set; }

    public static BillingEvent Receive(
        BillingProvider provider,
        ProviderReference providerEventId,
        string eventType,
        DateTimeOffset providerCreatedAt,
        DateTimeOffset receivedAt)
    {
        if (providerEventId.IsEmpty)
        {
            throw new DomainValidationException("A billing event needs a provider event id.")
            {
                Code = "billing.event.invalid",
            };
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainValidationException("A billing event needs a type.")
            {
                Code = "billing.event.invalid",
            };
        }

        return new BillingEvent(
            BillingEventId.New(),
            provider,
            providerEventId,
            eventType.Trim(),
            providerCreatedAt.ToUniversalTime(),
            receivedAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored event. Persistence only.</summary>
    public static BillingEvent Restore(
        BillingEventId id,
        BillingProvider provider,
        ProviderReference providerEventId,
        string eventType,
        ProviderReference? providerSubscriptionId,
        ProviderReference? providerCustomerId,
        ProviderReference? providerSessionId,
        DateTimeOffset providerCreatedAt,
        DateTimeOffset receivedAt,
        BillingEventStatus status,
        int attempts,
        DateTimeOffset? processedAt,
        string? failure) =>
        new(id, provider, providerEventId, eventType, providerCreatedAt, receivedAt)
        {
            ProviderSubscriptionId = providerSubscriptionId,
            ProviderCustomerId = providerCustomerId,
            ProviderSessionId = providerSessionId,
            Status = status,
            Attempts = attempts,
            ProcessedAt = processedAt,
            Failure = failure,
        };

    /// <summary>Records the references the event carried, if any.</summary>
    public void Concerns(
        ProviderReference? subscriptionId,
        ProviderReference? customerId,
        ProviderReference? sessionId)
    {
        ProviderSubscriptionId = subscriptionId;
        ProviderCustomerId = customerId;
        ProviderSessionId = sessionId;
    }

    /// <summary>Records that the event has been applied.</summary>
    public void Processed(DateTimeOffset now)
    {
        Status = BillingEventStatus.Processed;
        ProcessedAt = now.ToUniversalTime();
        Failure = null;
    }

    /// <summary>Records that nothing can be done with this event, ever.</summary>
    public void Unprocessable(string reason, DateTimeOffset now)
    {
        Status = BillingEventStatus.Unprocessable;
        ProcessedAt = now.ToUniversalTime();
        Failure = Shorten(reason);
    }

    /// <summary>Records a failure that is worth another attempt.</summary>
    public void Retry(string reason)
    {
        Status = BillingEventStatus.Pending;
        Attempts++;
        Failure = Shorten(reason);
    }

    /// <summary>
    /// Hands a claimed event back for another attempt after a transient failure.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Retry"/>, it does not count an attempt: claiming already
    /// did. Without it a transient failure would wait out the whole claim lease
    /// before any worker could try again.
    /// </remarks>
    public void Release(string reason)
    {
        Status = BillingEventStatus.Pending;
        Failure = Shorten(reason);
    }

    private static string Shorten(string reason) =>
        reason.Length <= 400 ? reason : reason[..400];
}
