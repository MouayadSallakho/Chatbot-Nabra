using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;
using Xunit.Abstractions;

namespace OmniAI.Infrastructure.IntegrationTests.Customers;

/// <summary>
/// Phase 35: the customer directory at 10k, 50k and 100k customers in one
/// workspace, measured against the Phase 34 query it replaced.
/// </summary>
/// <remarks>
/// Runs only with <c>OMNIAI_PERF_SCALE=1</c>: seeding 100k customers and 200k
/// conversations is not a unit test. Synthetic rows only. Results go to the test
/// output and, when <c>OMNIAI_PERF_OUT</c> is set, to <c>customer-directory-scale.txt</c>.
/// </remarks>
public sealed class CustomerDirectoryScaleTests(PostgresTestDatabase database, ITestOutputHelper output)
    : IClassFixture<PostgresTestDatabase>
{
    private const int Samples = 7;

    [ScaleFact]
    public async Task The_first_pages_cost_the_same_at_10k_50k_and_100k_customers()
    {
        TenantId tenant = await TenantAsync();

        // A second, noisy workspace: its rows must not count against this one.
        TenantId neighbour = await TenantAsync();
        await SeedAsync(neighbour, 20_000);

        List<string> lines = ["customers | new p1 ms | new p10 ms | old p1 ms | old p10 ms"];
        Dictionary<int, double> newFirstPage = [];
        int seeded = 0;

        foreach (int size in new[] { 10_000, 50_000, 100_000 })
        {
            await SeedAsync(tenant, size - seeded);
            seeded = size;

            // What autovacuum does in steady state (visibility map, statistics).
            // Separate commands: VACUUM cannot run inside a batch's transaction.
            await database.ExecuteScalarAsync("VACUUM ANALYZE customers");
            await database.ExecuteScalarAsync("VACUUM ANALYZE conversations");

            double new1 = await MedianAsync(() => NewPageAsync(tenant, 1));
            double new10 = await MedianAsync(() => NewPageAsync(tenant, 10));
            double old1 = await MedianAsync(() => OldPageAsync(tenant, 1));
            double old10 = await MedianAsync(() => OldPageAsync(tenant, 10));

            newFirstPage[size] = new1;

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{size} | {new1:0.0} | {new10:0.0} | {old1:0.0} | {old10:0.0}"));

            // The same page, both ways.
            Assert.Equal(await OldIdsAsync(tenant, 1), await NewIdsAsync(tenant, 1));
            Assert.Equal(await OldIdsAsync(tenant, 10), await NewIdsAsync(tenant, 10));
        }

        foreach (string line in lines)
        {
            output.WriteLine(line);
        }

        if (Environment.GetEnvironmentVariable("OMNIAI_PERF_OUT") is { Length: > 0 } directory)
        {
            await File.WriteAllLinesAsync(Path.Combine(directory, "customer-directory-scale.txt"), lines);
        }

        // Bounded: ten times the customers must not mean anything like ten times
        // the cost. Generous, because this runs on a laptop.
        Assert.True(
            newFirstPage[100_000] < Math.Max(3 * newFirstPage[10_000], 25),
            $"page 1 went from {newFirstPage[10_000]:0.0} ms at 10k to {newFirstPage[100_000]:0.0} ms at 100k");
    }

    private static async Task<double> MedianAsync(Func<Task> query)
    {
        await query(); // warm

        List<double> times = [];

        for (int i = 0; i < Samples; i++)
        {
            long started = Stopwatch.GetTimestamp();
            await query();
            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        times.Sort();

        return times[Samples / 2];
    }

    private async Task NewPageAsync(TenantId tenant, int page) => await NewIdsAsync(tenant, page);

    private async Task OldPageAsync(TenantId tenant, int page) => await OldIdsAsync(tenant, page);

    private async Task<List<CustomerId>> NewIdsAsync(TenantId tenant, int page)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return [.. (await new CustomerRepository(context).ListAsync(tenant, page, 20)).Items.Select(row => row.Id)];
    }

    /// <summary>The Phase 34 directory query, verbatim, as the baseline.</summary>
    private async Task<List<CustomerId>> OldIdsAsync(TenantId tenantId, int page)
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
                    ConversationCount = conversations.Count(),
                    LastActivityAt = conversations.Max(conversation => (DateTimeOffset?)conversation.LastActivityAt),
                })
            .OrderBy(row => row.LastActivityAt == null)
            .ThenByDescending(row => row.LastActivityAt)
            .ThenByDescending(row => row.Id)
            .Skip((page - 1) * 20)
            .Take(20)
            .Select(row => row.Id)
            .ToListAsync();
    }

    /// <summary>Adds customers with two conversations each, spread over a year.</summary>
    private async Task SeedAsync(TenantId tenant, int customers)
    {
        string t = tenant.Value.ToString("D", CultureInfo.InvariantCulture);

        await database.ExecuteScalarAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"""
             WITH added AS (
                 INSERT INTO customers (id, tenant_id, display_name, created_at, updated_at)
                 SELECT gen_random_uuid(), '{t}', NULL, now() - interval '400 days', now() - interval '400 days'
                   FROM generate_series(1, {customers})
                 RETURNING id
             )
             INSERT INTO conversations (id, tenant_id, customer_id, channel, status, handling, started_at, last_activity_at)
             SELECT gen_random_uuid(), '{t}', added.id, 'WebChat', 'Open', 'Ai', at, at
               FROM added
              CROSS JOIN generate_series(1, 2)
              CROSS JOIN LATERAL (SELECT now() - random() * interval '365 days' AS at) spread;
             SELECT 1
             """));
    }

    private async Task<TenantId> TenantAsync()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Scale"), TenantSlug.Create($"s{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();

        return tenant.Id;
    }
}

/// <summary>A fact that runs only when <c>OMNIAI_PERF_SCALE=1</c>.</summary>
public sealed class ScaleFactAttribute : FactAttribute
{
    public ScaleFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OMNIAI_PERF_SCALE") != "1")
        {
            Skip = "Scale measurements run only when OMNIAI_PERF_SCALE=1.";
        }
    }
}
