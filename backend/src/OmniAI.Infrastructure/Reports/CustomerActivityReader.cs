using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Analytics;
using OmniAI.Application.Reports;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Analytics;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.Reports;

/// <summary>
/// Per-customer activity, counted by PostgreSQL over the analytics base sets.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Same definitions as the dashboard.</strong> "Messages in the period"
/// and "conversations started in the period" are
/// <see cref="AnalyticsQueries"/>, unchanged; this only groups them by the
/// customer a conversation belongs to.
/// </para>
/// <para>
/// <strong>Canonical by construction.</strong> A merge moves conversations to the
/// surviving customer, so grouping by a conversation's customer yields each
/// person once.
/// </para>
/// <para>
/// <strong>Nothing personal is read.</strong> The customer's optional label and
/// the channels they used — never a WhatsApp number, a session credential or a
/// word of any message.
/// </para>
/// </remarks>
internal sealed class CustomerActivityReader(OmniAiDbContext context) : ICustomerActivityReader
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<CustomerActivityRow>> ReadAsync(
        TenantId tenantId,
        AnalyticsWindow window,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // Every message in the period, with the customer its conversation belongs
        // to. The base set is the dashboard's; only the grouping is this reader's.
        var sent =
            from message in AnalyticsQueries.MessagesSent(_context, tenantId, window)
            join conversation in _context.Conversations.AsNoTracking()
                on message.ConversationId equals conversation.Id
            select new
            {
                conversation.CustomerId,
                ConversationId = conversation.Id,
                conversation.Channel,
                message.Sender,
                message.SentAt,
            };

        // One grouped read, ordered and bounded by the database: the most recently
        // active customers first, with the id as a stable tie-breaker.
        var grouped = await sent
            .GroupBy(row => row.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                CustomerMessages = group.Count(row => row.Sender == MessageSender.Customer),
                FirstActivityAt = group.Min(row => row.SentAt),
                LastActivityAt = group.Max(row => row.SentAt),
            })
            .OrderByDescending(row => row.LastActivityAt)
            .ThenByDescending(row => row.CustomerId)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (grouped.Count == 0)
        {
            return [];
        }

        List<CustomerId> ids = [.. grouped.Select(row => row.CustomerId)];

        // Which conversations, on which channels, for exactly the customers above:
        // one row per active conversation, which the limit already bounds.
        var conversations = await sent
            .Where(row => ids.Contains(row.CustomerId))
            .Select(row => new { row.CustomerId, row.ConversationId, row.Channel })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<ActivityRow> active =
        [
            .. grouped.Select(row => new ActivityRow(
                row.CustomerId,
                row.CustomerMessages,
                conversations.Count(conversation => conversation.CustomerId == row.CustomerId),
                row.FirstActivityAt,
                row.LastActivityAt)),
        ];

        List<CustomerCount> started = await AnalyticsQueries
            .ConversationsStarted(_context, tenantId, window)
            .Where(conversation => ids.Contains(conversation.CustomerId))
            .GroupBy(conversation => conversation.CustomerId)
            .Select(group => new CustomerCount(group.Key, group.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<CustomerId, CustomerDisplayName?> names = await _context.Customers
            .AsNoTracking()
            .Where(customer => customer.TenantId == tenantId && ids.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, customer => customer.DisplayName, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. active.Select(row => new CustomerActivityRow(
                row.CustomerId,
                names.GetValueOrDefault(row.CustomerId),
                [
                    .. conversations
                        .Where(conversation => conversation.CustomerId == row.CustomerId)
                        .Select(conversation => conversation.Channel)
                        .Distinct()
                        .OrderBy(channel => channel),
                ],
                row.Conversations,
                started.Where(count => count.CustomerId == row.CustomerId).Sum(count => (long)count.Count),
                row.CustomerMessages,
                row.FirstActivityAt,
                row.LastActivityAt)),
        ];
    }

    private sealed record ActivityRow(
        CustomerId CustomerId,
        int CustomerMessages,
        int Conversations,
        DateTimeOffset FirstActivityAt,
        DateTimeOffset LastActivityAt);

    private sealed record CustomerCount(CustomerId CustomerId, int Count);
}
