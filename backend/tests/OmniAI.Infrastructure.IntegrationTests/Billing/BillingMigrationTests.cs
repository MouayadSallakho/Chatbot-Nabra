using Microsoft.EntityFrameworkCore;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Billing;

/// <summary>
/// What the Phase 25 migration left behind, on a database nothing else touched.
/// </summary>
/// <remarks>
/// Its own fixture, so these assertions are about the migration rather than
/// about whichever test happened to run first. A class that shared a database
/// with tests that seed offers could not tell the two apart.
/// </remarks>
public sealed class BillingMigrationTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    /// <remarks>
    /// <strong>Nobody who has not bought anything gets a row saying they did.</strong>
    /// The migration creates tables and one plan. It fabricates no subscription,
    /// no customer and no commercial relationship for the workspaces that already
    /// existed, and it invents no prices -- a price is a commercial decision and a
    /// provider identifier is deployment-specific.
    /// </remarks>
    [Fact]
    public async Task It_invented_no_subscriptions_no_customers_and_no_prices()
    {
        await using OmniAiDbContext context = database.CreateContext();

        Assert.False(await context.WorkspaceSubscriptions.AnyAsync());
        Assert.False(await context.WorkspaceBillingCustomers.AnyAsync());
        Assert.False(await context.BillingPlanOffers.AnyAsync());
        Assert.False(await context.BillingProviderPriceMappings.AnyAsync());
        Assert.False(await context.BillingCheckoutIntents.AnyAsync());
        Assert.False(await context.BillingEvents.AnyAsync());
    }
}
