using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Testing;

/// <summary>
/// Gives a conversation an owner that really exists, for tests whose subject is
/// something else.
/// </summary>
/// <remarks>
/// <para>
/// From Phase 30 a conversation carries a customer, and the database enforces
/// that the customer is a real one in the same workspace. A test about message
/// ordering or about optimistic concurrency still has to satisfy that, and
/// writing the same four lines into every such test would bury what each one is
/// actually asserting.
/// </para>
/// <para>
/// <strong>It deliberately does not invent identity.</strong> The customer it
/// creates is the one the conversation already names, dated from the
/// conversation&#39;s own start. Tests that are about identity resolution build
/// their customers themselves.
/// </para>
/// </remarks>
public static class TestCustomers
{
    /// <summary>
    /// Adds the customer a conversation names, unless this context already has
    /// it.
    /// </summary>
    /// <remarks>
    /// The local check matters: a test that stores several conversations for one
    /// customer through the same context would otherwise add the row twice and
    /// fail on the primary key rather than on whatever it meant to prove.
    /// </remarks>
    public static void AddOwner(this OmniAiDbContext context, Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(conversation);

        if (context.Customers.Local.Any(customer => customer.Id == conversation.CustomerId))
        {
            return;
        }

        context.Customers.Add(Customer.FromStored(
            conversation.CustomerId,
            conversation.TenantId,
            displayName: null,
            mergedIntoCustomerId: null,
            mergedAt: null,
            mergedBy: null,
            conversation.StartedAt,
            conversation.StartedAt));
    }
}
