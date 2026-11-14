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

        int skip = (page - 1) * pageSize;

        // The order is unchanged: latest conversation activity first, customers
        // with no history last, the id breaking ties. What changed (Phase 35) is
        // how the page is found -- see OrderedPageAsync.
        IReadOnlyList<CustomerId> ids =
            await OrderedPageAsync(tenantId, canonical, skip, pageSize, cancellationToken)
                .ConfigureAwait(false);

        if (ids.Count == 0)
        {
            return ([], total);
        }

        var customers = await canonical
            .Where(customer => ids.Contains(customer.Id))
            .Select(customer => new { customer.Id, customer.DisplayName, customer.CreatedAt })
            .ToDictionaryAsync(customer => customer.Id, cancellationToken)
            .ConfigureAwait(false);

        // The page's aggregates, for exactly the ids on it: an index range per
        // customer, not a pass over the workspace.
        var activity = await _context.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.TenantId == tenantId && ids.Contains(conversation.CustomerId))
            .GroupBy(conversation => conversation.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                Count = group.Count(),
                LastActivityAt = group.Max(conversation => conversation.LastActivityAt),
            })
            .ToDictionaryAsync(row => row.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<CustomerId, IReadOnlyList<ChannelKind>> channels =
            await ChannelsAsync(tenantId, [.. ids], cancellationToken).ConfigureAwait(false);

        IReadOnlyDictionary<CustomerId, string> references =
            await ReferencesAsync(tenantId, [.. ids], cancellationToken).ConfigureAwait(false);

        List<CustomerDirectoryRow> rows = new(ids.Count);

        foreach (CustomerId id in ids)
        {
            // Merged or removed between the two reads: left off this page rather
            // than shown half-described.
            if (!customers.TryGetValue(id, out var customer))
            {
                continue;
            }

            bool active = activity.TryGetValue(id, out var used);

            rows.Add(new CustomerDirectoryRow(
                customer.Id,
                customer.DisplayName,
                active ? used!.Count : 0,
                active ? used!.LastActivityAt : null,
                customer.CreatedAt,
                channels.TryGetValue(id, out IReadOnlyList<ChannelKind>? kinds) ? kinds : [],
                references.GetValueOrDefault(id)));
        }

        return (rows, total);
    }

    /// <summary>
    /// Each customer's newest WhatsApp number on the page, masked (Phase 39.1).
    /// </summary>
    /// <remarks>
    /// One statement for the page, through
    /// <c>ix_whatsapp_customer_identities_customer</c>: never one per row. Masking
    /// happens on the value object straight after the read, as on the detail's
    /// identities and the conversation reads, so no variable here holds a raw
    /// number. A customer with several numbers shows the one created last.
    /// </remarks>
    private async Task<IReadOnlyDictionary<CustomerId, string>> ReferencesAsync(
        TenantId tenantId,
        IReadOnlyList<CustomerId> customerIds,
        CancellationToken cancellationToken)
    {
        var numbers = await _context.WhatsAppCustomerIdentities
            .AsNoTracking()
            .Where(identity => identity.TenantId == tenantId && customerIds.Contains(identity.CustomerId))
            .OrderByDescending(identity => identity.CreatedAt)
            .Select(identity => new { identity.CustomerId, Number = identity.WhatsAppCustomerId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<CustomerId, string> newest = [];

        foreach (var entry in numbers)
        {
            newest.TryAdd(entry.CustomerId, entry.Number.Masked);
        }

        return newest;
    }

    /// <summary>
    /// A customer id as PostgreSQL orders <c>uuid</c>: by its canonical text,
    /// byte for byte. Ties are broken in memory here and must match the database.
    /// </summary>
    private static string PostgresUuidOrder(CustomerId id) =>
        id.Value.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// The ids on one page of the directory, in directory order, without ranking the
    /// whole workspace (Phase 35).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why.</strong> Ordering by each customer's latest conversation made
    /// PostgreSQL compute that maximum for every customer before taking twenty:
    /// linear in the workspace, about 3.6 µs a customer in Phase 34 and a projected
    /// ~360 ms a page at 100k. No index can serve a sort key that is an aggregate
    /// over another table.
    /// </para>
    /// <para>
    /// <strong>How.</strong> Walking conversations newest first, on the existing
    /// <c>(tenant_id, last_activity_at)</c> index, each customer first appears at
    /// their latest activity. So the first <c>skip + take</c> customers are all
    /// inside a short prefix of that walk. The prefix is widened until it holds
    /// enough customers; the activity of the last one needed is then a threshold,
    /// and every conversation at or after it is aggregated exactly — ties at the
    /// threshold included, so the order is the same as the full sort, not an
    /// approximation of it. Cost follows the depth of the page asked for, not the
    /// size of the workspace. No schema or write-path change was needed.
    /// </para>
    /// <para>
    /// Customers with no conversations (only possible after a merge moved their
    /// history away) come last, by id, exactly as before; they are only ever
    /// looked for once every active customer is already on an earlier position.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<CustomerId>> OrderedPageAsync(
        TenantId tenantId,
        IQueryable<Customer> canonical,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        int wanted = skip + take;

        // No join to the customers here, deliberately: a merge moves every
        // conversation to the surviving customer in the same transaction that
        // tombstones the source, so a conversation always names a canonical
        // customer. A semi-join "just in case" let the planner hash the whole
        // workspace's customers before taking the window -- the linear cost this
        // method exists to remove. The rows are still read from canonical
        // customers only (ListAsync), so a stray id could never be shown.
        IQueryable<Conversation> conversations = _context.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.TenantId == tenantId);

        DateTimeOffset? threshold = null;
        List<CustomerId> everyActive = [];

        for (int window = Math.Max(wanted * 4, 64); ; window = checked(window * 4))
        {
            var recent = await conversations
                .OrderByDescending(conversation => conversation.LastActivityAt)
                .Take(window)
                .Select(conversation => new { conversation.CustomerId, conversation.LastActivityAt })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // First appearance in newest-first order is each customer's latest.
            List<(CustomerId Id, DateTimeOffset Latest)> seen = [];
            HashSet<CustomerId> distinct = [];

            foreach (var row in recent)
            {
                if (distinct.Add(row.CustomerId))
                {
                    seen.Add((row.CustomerId, row.LastActivityAt));
                }
            }

            if (seen.Count >= wanted)
            {
                threshold = seen[wanted - 1].Latest;

                break;
            }

            if (recent.Count < window)
            {
                // The whole workspace's history is in hand: every active customer.
                everyActive = [.. seen.Select(customer => customer.Id)];

                break;
            }
        }

        if (threshold is { } since)
        {
            var ranked = await conversations
                .Where(conversation => conversation.LastActivityAt >= since)
                .GroupBy(conversation => conversation.CustomerId)
                .Select(group => new
                {
                    Id = group.Key,
                    Latest = group.Max(conversation => conversation.LastActivityAt),
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return
            [
                .. ranked
                    .OrderByDescending(row => row.Latest)
                    .ThenByDescending(row => PostgresUuidOrder(row.Id), StringComparer.Ordinal)
                    .Skip(skip)
                    .Take(take)
                    .Select(row => row.Id),
            ];
        }

        // Fewer active customers than the page reaches: re-rank them exactly (the
        // walk's order has no id tie-break), then continue with the inactive ones.
        var active = await conversations
            .GroupBy(conversation => conversation.CustomerId)
            .Select(group => new
            {
                Id = group.Key,
                Latest = group.Max(conversation => conversation.LastActivityAt),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<CustomerId> ordered =
        [
            .. active
                .OrderByDescending(row => row.Latest)
                .ThenByDescending(row => PostgresUuidOrder(row.Id), StringComparer.Ordinal)
                .Select(row => row.Id),
        ];

        List<CustomerId> result = [.. ordered.Skip(skip).Take(take)];

        if (result.Count < take)
        {
            int inactiveSkip = Math.Max(0, skip - ordered.Count);

            List<CustomerId> inactive = await canonical
                .Where(customer => !_context.Conversations.Any(conversation =>
                    conversation.TenantId == tenantId && conversation.CustomerId == customer.Id))
                .OrderByDescending(customer => customer.Id)
                .Skip(inactiveSkip)
                .Take(take - result.Count)
                .Select(customer => customer.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            result.AddRange(inactive);
        }

        return result;
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
            throw CustomerMergeNotAllowedException.Self();
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
        ?? throw CustomerMergeNotAllowedException.NotInWorkspace(
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
