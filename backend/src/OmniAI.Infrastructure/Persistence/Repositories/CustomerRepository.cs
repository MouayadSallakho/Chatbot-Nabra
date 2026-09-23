using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Customers;
using OmniAI.Application.Customers.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The canonical customer directory, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every aggregate is computed by the database.</strong> The directory
/// needs a conversation count, a last-activity maximum and the set of channels
/// used, for twenty customers at a time. Loading the customers and then their
/// conversations would be twenty-one queries and would read a workspace's whole
/// history to render one page, so the counts are a grouped join and the channels
/// are one further query over exactly the ids on the page.
/// </para>
/// <para>
/// <strong>The merge is the only method that locks.</strong> It is also the only
/// one that writes more than one row, and the two facts are the same fact.
/// </para>
/// </remarks>
internal sealed class CustomerRepository(OmniAiDbContext context) : ICustomerRepository
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<(IReadOnlyList<CustomerDirectoryRow> Items, int TotalCount)> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Customer> canonical = _context.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.TenantId == tenantId && customer.MergedIntoCustomerId == null);

        int total = await canonical.CountAsync(cancellationToken).ConfigureAwait(false);

        // A left join with aggregates: one round trip, and PostgreSQL does the
        // counting. A customer with no conversations at all -- possible only after
        // a merge moved their history away -- still appears, with a count of zero.
        var page1 = await canonical
            .GroupJoin(
                _context.Conversations.AsNoTracking()
                    .Where(conversation => conversation.TenantId == tenantId),
                customer => customer.Id,
                conversation => conversation.CustomerId,
                (customer, conversations) => new
                {
                    customer.Id,
                    customer.DisplayName,
                    customer.CreatedAt,
                    ConversationCount = conversations.Count(),
                    LastActivityAt = conversations
                        .Max(conversation => (DateTimeOffset?)conversation.LastActivityAt),
                })
            // Nulls last, explicitly. PostgreSQL sorts them first on a descending
            // order, which would put every customer who has no history at the top
            // of a screen whose whole purpose is recent activity.
            .OrderBy(row => row.LastActivityAt == null)
            .ThenByDescending(row => row.LastActivityAt)
            .ThenByDescending(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<CustomerId, IReadOnlyList<ChannelKind>> channels =
            await ChannelsAsync(
                    tenantId,
                    [.. page1.Select(row => row.Id)],
                    cancellationToken)
                .ConfigureAwait(false);

        return (
            [.. page1.Select(row => new CustomerDirectoryRow(
                row.Id,
                row.DisplayName,
                row.ConversationCount,
                row.LastActivityAt,
                row.CreatedAt,
                channels.TryGetValue(row.Id, out IReadOnlyList<ChannelKind>? kinds) ? kinds : []))],
            total);
    }

    public async Task<Versioned<Customer>?> FindAsync(
        TenantId tenantId,
        CustomerId customerId,
        CancellationToken cancellationToken = default)
    {
        Customer? customer = await _context.Customers
            .FirstOrDefaultAsync(
                candidate => candidate.Id == customerId && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        return customer is null ? null : new Versioned<Customer>(customer, VersionOf(customer));
    }

    public async Task<CustomerDirectoryRow?> SummariseAsync(
        TenantId tenantId,
        CustomerId customerId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _context.Customers
            .AsNoTracking()
            .Where(customer => customer.Id == customerId && customer.TenantId == tenantId)
            .Select(customer => new
            {
                customer.Id,
                customer.DisplayName,
                customer.CreatedAt,
                ConversationCount = _context.Conversations
                    .Count(conversation =>
                        conversation.TenantId == tenantId
                        && conversation.CustomerId == customerId),
                LastActivityAt = _context.Conversations
                    .Where(conversation =>
                        conversation.TenantId == tenantId
                        && conversation.CustomerId == customerId)
                    .Max(conversation => (DateTimeOffset?)conversation.LastActivityAt),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (summary is null)
        {
            return null;
        }

        IReadOnlyDictionary<CustomerId, IReadOnlyList<ChannelKind>> channels =
            await ChannelsAsync(tenantId, [customerId], cancellationToken).ConfigureAwait(false);

        return new CustomerDirectoryRow(
            summary.Id,
            summary.DisplayName,
            summary.ConversationCount,
            summary.LastActivityAt,
            summary.CreatedAt,
            channels.TryGetValue(customerId, out IReadOnlyList<ChannelKind>? kinds) ? kinds : []);
    }

    public async Task<IReadOnlyList<CustomerChannelIdentityView>> ListIdentitiesAsync(
        TenantId tenantId,
        CustomerId customerId,
        CancellationToken cancellationToken = default)
    {
        // Masking happens on the value object, after the read. Projecting the raw
        // column into an anonymous string here would create a variable holding an
        // unmasked number, which is exactly the thing that ends up in a log.
        List<Domain.WhatsApp.WhatsAppCustomerId> numbers = await _context
            .WhatsAppCustomerIdentities
            .AsNoTracking()
            .Where(identity =>
                identity.TenantId == tenantId && identity.CustomerId == customerId)
            .OrderBy(identity => identity.CreatedAt)
            .Select(identity => identity.WhatsAppCustomerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. numbers.Select(number => new CustomerChannelIdentityView(
                ChannelKind.WhatsApp,
                number.Masked)),
        ];
    }

    public async Task<IReadOnlyList<CustomerConversationView>> ListConversationsAsync(
        TenantId tenantId,
        CustomerId customerId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await _context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.TenantId == tenantId && conversation.CustomerId == customerId)
            .OrderByDescending(conversation => conversation.LastActivityAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(limit)
            .Select(conversation => new CustomerConversationView(
                conversation.Id.Value,
                conversation.Channel,
                conversation.Status,
                conversation.Handling,
                conversation.StartedAt,
                conversation.LastActivityAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<ConcurrencyToken> UpdateAsync(
        Customer customer,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        // The version the caller read goes into the UPDATE's WHERE clause, so the
        // database decides whether the row is still what they believed.
        RowVersion(customer).OriginalValue = Decode(expected);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CustomerVersionMismatchException();
        }

        return VersionOf(customer);
    }

    public async Task<(CustomerMergeCounts Counts, bool AlreadyMerged)> MergeAsync(
        TenantId tenantId,
        CustomerId sourceId,
        CustomerId targetId,
        UserId mergedBy,
        ConcurrencyToken expected,
        DateTimeOffset mergedAt,
        CancellationToken cancellationToken = default)
    {
        if (sourceId == targetId)
        {
            throw new CustomerMergeNotAllowedException("A customer cannot be merged into itself.");
        }

        await using IDbContextTransaction transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Both locks, in an order derived from the ids rather than from which one
        // the caller called the source. Two merges naming the same pair from
        // opposite directions therefore queue behind each other; taking them in
        // caller order would have each holding the lock the other is waiting for.
        (CustomerId first, CustomerId second) = sourceId.Value.CompareTo(targetId.Value) <= 0
            ? (sourceId, targetId)
            : (targetId, sourceId);

        await LockAsync(first, cancellationToken).ConfigureAwait(false);
        await LockAsync(second, cancellationToken).ConfigureAwait(false);

        // Read after locking, never before: the decision to merge was made against
        // a version somebody may since have changed, and the only reading that
        // proves anything is the reading nobody can invalidate.
        Customer source = await RequireAsync(tenantId, sourceId, cancellationToken)
            .ConfigureAwait(false);

        Customer target = await RequireAsync(tenantId, targetId, cancellationToken)
            .ConfigureAwait(false);

        if (source.MergedIntoCustomerId == targetId)
        {
            // Already done, by an earlier attempt or by the other half of a
            // double-click. Answering "done" is the only response that does not
            // invite the caller to try something else, and nothing is moved twice.
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return (new CustomerMergeCounts(0, 0), true);
        }

        if (VersionOf(source) != expected)
        {
            throw new CustomerVersionMismatchException();
        }

        // Throws for an already-merged source, an already-merged target, a
        // cross-workspace pair and a self-merge. Checked while both rows are
        // locked, so the answer cannot be stale by the time it is acted on -- and
        // "only an unmerged record may be merged, only into an unmerged record" is
        // what makes a cycle impossible rather than merely unlikely.
        source.MergeInto(target, mergedBy, mergedAt);

        // Set-based, because a customer's history can be long and a merge must not
        // cost one round trip per conversation. Ordering, timestamps, sequence
        // numbers and message rows are untouched: this changes one column.
        int identities = await _context.WhatsAppCustomerIdentities
            .Where(identity =>
                identity.TenantId == tenantId && identity.CustomerId == sourceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(identity => identity.CustomerId, targetId),
                cancellationToken)
            .ConfigureAwait(false);

        int conversations = await _context.Conversations
            .Where(conversation =>
                conversation.TenantId == tenantId && conversation.CustomerId == sourceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(conversation => conversation.CustomerId, targetId),
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CustomerVersionMismatchException();
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return (new CustomerMergeCounts(conversations, identities), false);
    }

    /// <summary>The distinct channels each of these customers has used.</summary>
    /// <remarks>
    /// One query for the whole page. Asking per customer would be the N+1 this
    /// method exists to avoid, and asking for every customer in the workspace
    /// would read rows nothing on screen needs.
    /// </remarks>
    private async Task<IReadOnlyDictionary<CustomerId, IReadOnlyList<ChannelKind>>> ChannelsAsync(
        TenantId tenantId,
        IReadOnlyList<CustomerId> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<CustomerId, IReadOnlyList<ChannelKind>>();
        }

        var pairs = await _context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.TenantId == tenantId
                && customerIds.Contains(conversation.CustomerId))
            .Select(conversation => new { conversation.CustomerId, conversation.Channel })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return pairs
            .GroupBy(pair => pair.CustomerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ChannelKind>)[.. group.Select(pair => pair.Channel).Order()]);
    }

    private async Task<Customer> RequireAsync(
        TenantId tenantId,
        CustomerId customerId,
        CancellationToken cancellationToken) =>
        await _context.Customers
            .FirstOrDefaultAsync(
                customer => customer.Id == customerId && customer.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false)
        ?? throw new CustomerMergeNotAllowedException(
            "Both customers must exist in this workspace.");

    /// <summary>
    /// Takes the transaction-scoped lock for one customer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transaction-scoped, so it is released by the commit or the rollback and
    /// there is no path on which it leaks. An advisory lock rather than
    /// <c>SELECT … FOR UPDATE</c> because every writer that could conflict — there
    /// is exactly one, this method — takes it, and because it can be taken in a
    /// chosen order before anything is read.
    /// </para>
    /// <para>
    /// A rename racing a merge is not covered by this lock and does not need to
    /// be: both carry the row version, so whichever commits second is told its
    /// version is stale.
    /// </para>
    /// </remarks>
    private async Task LockAsync(CustomerId customerId, CancellationToken cancellationToken) =>
        await _context.Database
            .ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})",
                [KeyFor(customerId)],
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// A stable 64-bit key for one customer.
    /// </summary>
    /// <remarks>
    /// SHA-256 of the id rather than <c>GetHashCode</c>, which is randomised per
    /// process: two web servers would otherwise compute different keys for the
    /// same customer and would not exclude each other at all. The literal prefix
    /// namespaces it away from every other advisory lock in the platform.
    /// </remarks>
    private static long KeyFor(CustomerId customerId)
    {
        Span<byte> digest = stackalloc byte[32];

        SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"customer-merge:{customerId.Value:D}")),
            digest);

        return BitConverter.ToInt64(digest[..8]);
    }

    private ConcurrencyToken VersionOf(Customer customer) =>
        RowVersionTokens.Encode(RowVersion(customer).CurrentValue);

    /// <summary>
    /// Reads a caller-supplied token back into the number PostgreSQL stores.
    /// </summary>
    /// <remarks>
    /// A token this platform did not issue cannot match any row, so it is treated
    /// as a stale version rather than as a malformed request.
    /// </remarks>
    private static uint Decode(ConcurrencyToken expected) =>
        RowVersionTokens.TryDecode(expected, out uint rowVersion) ? rowVersion : 0u;

    private PropertyEntry<Customer, uint> RowVersion(Customer customer) =>
        _context.Entry(customer).Property<uint>(PersistenceSchema.XminConcurrencyToken);
}
