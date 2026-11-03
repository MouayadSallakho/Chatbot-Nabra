using System.Globalization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WhatsApp;

/// <summary>
/// The identity of one contact mapping.
/// </summary>
public readonly record struct WhatsAppContactId
{
    private WhatsAppContactId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static WhatsAppContactId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    public static WhatsAppContactId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("WhatsApp contact id must not be an empty GUID.")
                {
                    Code = "whatsapp.contact.invalid",
                }
            : new WhatsAppContactId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// Which conversation one customer's messages on one connection belong to.
/// </summary>
/// <remarks>
/// <strong>A person messaging a business is a continuing relationship, and a
/// conversation is an episode of it.</strong> WhatsApp has no notion of a thread
/// ending, so this platform decides: while the mapped conversation is open, every
/// message from that customer joins it; once it is closed, the next message
/// starts a new one. Mixing a new question into a conversation somebody closed
/// last month would resurrect a settled transcript; starting a fresh one per
/// message would shred a single exchange into fragments.
/// <para>
/// <strong>One current mapping per (connection, customer), enforced by the
/// database.</strong> Two webhooks arriving together must not create two
/// conversations for one person: the unique index on the current row settles the
/// race, and the loser reads the winner's mapping.
/// </para>
/// <para>
/// The workspace is carried here, not inferred. It comes from the connection the
/// webhook was routed to, and never from anything in the payload.
/// </para>
/// </remarks>
public sealed class WhatsAppContact
{
    private WhatsAppContact(
        WhatsAppContactId id,
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        WhatsAppCustomerId customerId,
        ConversationId conversationId,
        DateTimeOffset startedAt)
    {
        Id = id;
        ConnectionId = connectionId;
        TenantId = tenantId;
        CustomerId = customerId;
        ConversationId = conversationId;
        StartedAt = startedAt;
        IsCurrent = true;
    }

    /// <summary>Identity.</summary>
    public WhatsAppContactId Id { get; }

    /// <summary>The connection this customer wrote to.</summary>
    public WhatsAppConnectionId ConnectionId { get; }

    /// <summary>The owning workspace, taken from the connection.</summary>
    public TenantId TenantId { get; }

    /// <summary>Who wrote. Personal data; masked wherever it is shown.</summary>
    public WhatsAppCustomerId CustomerId { get; }

    /// <summary>The conversation their messages join.</summary>
    public ConversationId ConversationId { get; }

    /// <summary>When this mapping began, in UTC.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Whether this is the mapping new messages join.
    /// </summary>
    /// <remarks>
    /// Superseded mappings are kept rather than deleted: they are the record of
    /// which conversation a message from six months ago belonged to, and losing
    /// that would make an old transcript unattributable.
    /// </remarks>
    public bool IsCurrent { get; private set; }

    /// <summary>Starts a mapping from a customer to a conversation.</summary>
    public static WhatsAppContact Start(
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        WhatsAppCustomerId customerId,
        ConversationId conversationId,
        DateTimeOffset startedAt)
    {
        if (connectionId.IsEmpty)
        {
            throw new DomainValidationException("A contact must belong to a connection.")
            {
                Code = "whatsapp.contact.invalid",
            };
        }

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A contact must belong to a workspace.")
            {
                Code = "whatsapp.contact.invalid",
            };
        }

        if (conversationId.IsEmpty)
        {
            throw new DomainValidationException("A contact must name a conversation.")
            {
                Code = "whatsapp.contact.invalid",
            };
        }

        if (startedAt == default)
        {
            throw new DomainValidationException("The start instant must be a real point in time.")
            {
                Code = "whatsapp.contact.invalid",
            };
        }

        return new WhatsAppContact(
            WhatsAppContactId.New(),
            connectionId,
            tenantId,
            customerId,
            conversationId,
            startedAt.ToUniversalTime());
    }

    /// <summary>Rehydrates a stored mapping. Persistence only.</summary>
    public static WhatsAppContact FromStored(
        WhatsAppContactId id,
        WhatsAppConnectionId connectionId,
        TenantId tenantId,
        WhatsAppCustomerId customerId,
        ConversationId conversationId,
        DateTimeOffset startedAt,
        bool isCurrent) =>
        new(id, connectionId, tenantId, customerId, conversationId, startedAt)
        {
            IsCurrent = isCurrent,
        };

    /// <summary>
    /// Stops this mapping being the one new messages join.
    /// </summary>
    /// <remarks>
    /// Called when the conversation it points at has been closed and the next
    /// message needs a new one. The row stays; only its currency changes.
    /// </remarks>
    public void Supersede() => IsCurrent = false;
}
