using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Customers.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Customers;

/// <summary>
/// Phase 35: the bounded customer-directory query returns exactly what the full
/// sort it replaced returned, page for page.
/// </summary>
/// <remarks>
/// The reference is the Phase 34 query, kept here verbatim: every customer's
/// latest conversation activity computed by the database, nulls last, the id
/// breaking ties. The data is built to hit every edge the new query reasons
/// about: exact ties across customers (including at page boundaries), customers
/// with many conversations, customers with none, and merged tombstones.
/// </remarks>
public sealed class CustomerDirectoryOrderTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(20)]
    [InlineData(100)]
    public async Task Every_page_matches_the_full_sort_it_replaced(int pageSize)
    {
        TenantId tenant = await SeedAsync();

        List<CustomerId> expected = await ReferenceOrderAsync(tenant);
        List<CustomerId> actual = [];

        await using OmniAiDbContext context = database.CreateContext();

        CustomerRepository repository = new(context);

        for (int page = 1; ; page++)
        {
            (IReadOnlyList<CustomerDirectoryRow> items, int total) = await repository.ListAsync(tenant, page, pageSize);

            Assert.Equal(expected.Count, total);

            if (items.Count == 0)
            {
                break;
            }

            actual.AddRange(items.Select(item => item.Id));
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Each_row_still_reports_its_whole_history_not_only_the_window_that_found_it()
    {
        TenantId tenant = await SeedAsync();

        await using OmniAiDbContext context = database.CreateContext();

        (IReadOnlyList<CustomerDirectoryRow> items, _) = await new CustomerRepository(context).ListAsync(tenant, 1, 5);

        foreach (CustomerDirectoryRow row in items)
        {
            List<Conversation> history = await context.Conversations
                .AsNoTracking()
                .Where(conversation => conversation.TenantId == tenant && conversation.CustomerId == row.Id)
                .ToListAsync();

            Assert.Equal(history.Count, row.ConversationCount);
            Assert.Equal(history.Max(conversation => conversation.LastActivityAt), row.LastActivityAt);
        }
    }

    [Fact]
    public async Task A_workspace_without_customers_has_an_empty_directory()
    {
        TenantId tenant = await TenantAsync();

        await using OmniAiDbContext context = database.CreateContext();

        (IReadOnlyList<CustomerDirectoryRow> items, int total) = await new CustomerRepository(context).ListAsync(tenant, 1, 20);

        Assert.Empty(items);
        Assert.Equal(0, total);
    }

    /// <summary>The Phase 34 query, unchanged: the definition of the right order.</summary>
    private async Task<List<CustomerId>> ReferenceOrderAsync(TenantId tenantId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.Customers
            .AsNoTracking()
            .Where(customer => customer.TenantId == tenantId && customer.MergedIntoCustomerId == null)
            .GroupJoin(
                context.Conversations.AsNoTracking().Where(conversation => conversation.TenantId == tenantId),
                customer => customer.Id,
                conversation => conversation.CustomerId,
                (customer, conversations) => new
                {
                    customer.Id,
                    LastActivityAt = conversations.Max(conversation => (DateTimeOffset?)conversation.LastActivityAt),
                })
            .OrderBy(row => row.LastActivityAt == null)
            .ThenByDescending(row => row.LastActivityAt)
            .ThenByDescending(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync();
    }

    /// <summary>
    /// 90 active customers (one to five conversations each, activity in blocks of
    /// three identical instants so ties cross every page size), 12 with no history
    /// and 6 merged tombstones.
    /// </summary>
    private async Task<TenantId> SeedAsync()
    {
        TenantId tenant = await TenantAsync();

        await using OmniAiDbContext context = database.CreateContext();

        List<Customer> customers = [];

        for (int i = 0; i < 90; i++)
        {
            Customer customer = Customer.Create(tenant, displayName: null, Noon.AddDays(-30));
            customers.Add(customer);
            context.Customers.Add(customer);

            // Latest activity: blocks of three customers share one instant exactly.
            DateTimeOffset latest = Noon.AddMinutes(-(i / 3) * 10);
            int count = 1 + (i % 5);

            for (int c = 0; c < count; c++)
            {
                context.Conversations.Add(
                    Conversation.Start(tenant, customer.Id, ChannelKind.WebChat, latest.AddHours(-c * 7)));
            }
        }

        for (int i = 0; i < 12; i++)
        {
            context.Customers.Add(Customer.Create(tenant, displayName: null, Noon.AddDays(-30)));
        }

        await context.SaveChangesAsync();

        // Tombstones: merged away, so never listed, whatever their history.
        for (int i = 0; i < 6; i++)
        {
            Customer source = Customer.Create(tenant, displayName: null, Noon.AddDays(-30));
            context.Customers.Add(source);
            await context.SaveChangesAsync();

            context.Conversations.Add(Conversation.Start(tenant, source.Id, ChannelKind.WebChat, Noon.AddMinutes(-1)));
            await context.SaveChangesAsync();

            await new CustomerRepository(context).MergeAsync(
                tenant,
                source.Id,
                customers[i * 10].Id,
                Domain.Users.UserId.New(),
                (await new CustomerRepository(context).FindAsync(tenant, source.Id))!.Version,
                Noon);

            context.ChangeTracker.Clear();
        }

        return tenant;
    }

    private async Task<TenantId> TenantAsync()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Directory"), TenantSlug.Create($"d{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();

        return tenant.Id;
    }
}
