using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WhatsApp;

/// <summary>
/// The durable link between one WhatsApp number and the person this workspace
/// knows it as.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this is not <see cref="WhatsAppContact"/>.</strong> A contact row
/// records one conversation: it is created when a conversation starts, superseded
/// when the next one does, and there are as many of them as the person has had
/// conversations. Identity is the opposite — exactly one row per person per
/// workspace, for as long as the workspace exists. Hanging the canonical customer
/// off the contact row would mean the answer to "who is this?" lived in N places
/// that could disagree, and no constraint could stop them.
/// </para>
/// <para>
/// <strong>Keyed on the workspace and the customer's own number, not on the
/// connection.</strong> A workspace may run several business numbers; somebody
/// messaging two of them is one person, and the platform knows that because Meta
/// hands back the same sender identity, not because two strings look alike. That
/// is a transport fact, and it is the only kind of identity claim this platform
/// makes on its own. Everything else — same name, similar number, same device —
/// is refused by design.
/// </para>
/// <para>
/// <strong>The workspace is in the key, so the same number in two workspaces is
/// two people.</strong> It has to be: they are two businesses' customer records,
/// and neither may read the other's.
/// </para>
/// </remarks>
public sealed class WhatsAppCustomerIdentity
{
    private WhatsAppCustomerIdentity(
        TenantId tenantId,
        WhatsAppCustomerId whatsAppCustomerId,
        CustomerId customerId,
        DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        WhatsAppCustomerId = whatsAppCustomerId;
        CustomerId = customerId;
        CreatedAt = createdAt;
    }

    /// <summary>The owning workspace. Half of the key.</summary>
    public TenantId TenantId { get; }

    /// <summary>
    /// The sender identity Meta reports. Personal data; masked wherever shown.
    /// </summary>
    public WhatsAppCustomerId WhatsAppCustomerId { get; }

    /// <summary>
    /// The canonical customer.
    /// </summary>
    /// <remarks>
    /// Mutable for exactly one operation: an administrator merging two customers,
    /// which repoints this identity at the surviving record. Nothing automatic
    /// may change it.
    /// </remarks>
    public CustomerId CustomerId { get; private set; }

    /// <summary>When this number was first seen in this workspace.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Records a number seen for the first time.</summary>
    public static WhatsAppCustomerIdentity Create(
        TenantId tenantId,
        WhatsAppCustomerId whatsAppCustomerId,
        CustomerId customerId,
        DateTimeOffset createdAt)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException(
                "A WhatsApp customer identity must belong to a workspace.");
        }

        if (customerId.IsEmpty)
        {
            throw new DomainValidationException(
                "A WhatsApp customer identity must point at a customer.");
        }

        if (createdAt == default)
        {
            throw new DomainValidationException(
                "A WhatsApp customer identity needs the instant it was first seen.");
        }

        return new WhatsAppCustomerIdentity(
            tenantId,
            whatsAppCustomerId,
            customerId,
            createdAt);
    }

    /// <summary>Rehydrates a stored identity. Persistence only.</summary>
    public static WhatsAppCustomerIdentity FromStored(
        TenantId tenantId,
        WhatsAppCustomerId whatsAppCustomerId,
        CustomerId customerId,
        DateTimeOffset createdAt) =>
        new(tenantId, whatsAppCustomerId, customerId, createdAt);

    /// <summary>Repoints this identity at the customer a merge made canonical.</summary>
    public void MoveToCustomer(CustomerId customerId)
    {
        if (customerId.IsEmpty)
        {
            throw new DomainValidationException(
                "A WhatsApp customer identity must point at a customer.");
        }

        CustomerId = customerId;
    }
}
