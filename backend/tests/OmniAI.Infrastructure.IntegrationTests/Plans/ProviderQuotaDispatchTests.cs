using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OmniAI.Application.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Plans;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Plans;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.Ai.Usage;
using OmniAI.Infrastructure.IntegrationTests.Ai.OpenAi;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Plans;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Plans;

/// <summary>
/// The monthly provider quota, at the boundary where money is spent.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here reaches OpenAI.</strong> The real adapter is wired to a
/// canned transport that counts requests and answers from a string, so "the
/// provider received exactly one request" is a fact this suite can assert rather
/// than a hope.
/// </para>
/// <para>
/// <strong>Phase 23's rule and Phase 24's sit together.</strong> If the Started
/// row cannot be committed, no provider request is made; if the quota is
/// exhausted, no row is written <em>and</em> no provider request is made. Both
/// are checked by counting what the transport saw.
/// </para>
/// </remarks>
public sealed class ProviderQuotaDispatchTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Under_the_quota_the_call_reaches_the_provider_and_is_accounted()
    {
        PlanWorld world = await New(monthlyOperations: 5);

        CannedOpenAiTransport transport = new CannedOpenAiTransport
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        OpenAiTextGenerator generator = Generator(world, transport);

        AiTextGenerationResult result = await generator.GenerateAsync(
            OpenAiTestHarness.SimpleRequest(),
            AiUsageContext.For(world.Tenant.Id, AiUsagePurpose.ConversationGeneration));

        Assert.Equal("Certainly.", result.Text);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(1, await world.CountOperationsAsync());
    }

    /// <remarks>
    /// The rule that makes the quota a cost control rather than a report: an
    /// exhausted workspace spends nothing, because nothing is dispatched.
    /// </remarks>
    [Fact]
    public async Task An_exhausted_quota_writes_no_row_and_makes_no_provider_request()
    {
        PlanWorld world = await New(monthlyOperations: 2);

        await world.AddOperationsAsync(2, Noon);

        CannedOpenAiTransport transport = new CannedOpenAiTransport
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        OpenAiTextGenerator generator = Generator(world, transport);

        EntitlementExceededException refused =
            await Assert.ThrowsAsync<EntitlementExceededException>(() => generator.GenerateAsync(
                OpenAiTestHarness.SimpleRequest(),
                AiUsageContext.For(world.Tenant.Id, AiUsagePurpose.ConversationGeneration)));

        Assert.Equal(EntitlementKind.MonthlyProviderOperations, refused.Kind);
        Assert.NotNull(refused.Period);

        // Zero requests and zero new rows. The ledger's meaning is preserved: a
        // row is evidence that something reached the paid boundary, and this
        // never did.
        Assert.Equal(0, transport.Calls);
        Assert.Equal(2, await world.CountOperationsAsync());
    }

    [Fact]
    public async Task A_quota_of_zero_refuses_before_the_first_call()
    {
        PlanWorld world = await New(monthlyOperations: 0);

        CannedOpenAiTransport transport = new CannedOpenAiTransport
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        await Assert.ThrowsAsync<EntitlementExceededException>(
            () => Generator(world, transport).GenerateAsync(
                OpenAiTestHarness.SimpleRequest(),
                AiUsageContext.For(world.Tenant.Id, AiUsagePurpose.ConversationGeneration)));

        Assert.Equal(0, transport.Calls);
        Assert.Equal(0, await world.CountOperationsAsync());
    }

    [Fact]
    public async Task An_unlimited_plan_dispatches_as_it_always_did()
    {
        PlanWorld world = await New();

        CannedOpenAiTransport transport = new CannedOpenAiTransport
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        OpenAiTextGenerator generator = Generator(world, transport);

        for (int index = 0; index < 4; index++)
        {
            await generator.GenerateAsync(
                OpenAiTestHarness.SimpleRequest(),
                AiUsageContext.For(world.Tenant.Id, AiUsagePurpose.ConversationGeneration));
        }

        Assert.Equal(4, transport.Calls);
        Assert.Equal(4, await world.CountOperationsAsync());
    }

    /// <remarks>
    /// <strong>The overshoot test.</strong> Nine of ten used, two requests at
    /// once: one must reach the provider and one must not. Counting outside a
    /// transaction would let both through and the workspace would be billed for
    /// eleven.
    /// </remarks>
    [Fact]
    public async Task Two_concurrent_dispatches_at_the_last_operation_send_exactly_one()
    {
        PlanWorld world = await New(monthlyOperations: 10);

        await world.AddOperationsAsync(9, Noon);

        CannedOpenAiTransport transport = new CannedOpenAiTransport
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        // One transport, two generators: whatever reaches the provider is counted
        // in one place.
        OpenAiTextGenerator first = Generator(world, transport);
        OpenAiTextGenerator second = Generator(world, transport);

        Exception?[] outcomes = await Outcomes(
            Dispatch(first, world),
            Dispatch(second, world));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is EntitlementExceededException);

        // Exactly one paid request, and exactly ten accounted operations.
        Assert.Equal(1, transport.Calls);
        Assert.Equal(10, await world.CountOperationsAsync());
    }

    [Fact]
    public async Task The_quota_is_this_workspaces_and_not_the_platforms()
    {
        PlanWorld mine = await New(monthlyOperations: 1);
        PlanWorld theirs = await New(monthlyOperations: 1);

        await mine.AddOperationsAsync(1, Noon);

        CannedOpenAiTransport transport = new CannedOpenAiTransport
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        await Assert.ThrowsAsync<EntitlementExceededException>(
            () => Dispatch(Generator(mine, transport), mine));

        // Their workspace is unaffected by mine being exhausted.
        await Dispatch(Generator(theirs, transport), theirs);

        Assert.Equal(1, transport.Calls);
        Assert.Equal(1, await theirs.CountOperationsAsync());
    }

    [Fact]
    public async Task Next_month_the_same_workspace_may_dispatch_again()
    {
        PlanWorld world = await New(monthlyOperations: 1);

        await world.AddOperationsAsync(1, new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero));

        CannedOpenAiTransport transport = new CannedOpenAiTransport
        {
            ResponseBody = OpenAiCannedResponses.Completed("Certainly."),
        };

        await Assert.ThrowsAsync<EntitlementExceededException>(
            () => Dispatch(Generator(world, transport, Noon), world));

        DateTimeOffset october = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        await Dispatch(Generator(world, transport, october), world);

        Assert.Equal(1, transport.Calls);

        // September's row is still there. The allowance resets; the history does
        // not move.
        Assert.Equal(2, await world.CountOperationsAsync());
    }

    private async Task<PlanWorld> New(int? monthlyOperations = null) =>
        await new PlanWorld(database).StartAsync(monthlyOperations: monthlyOperations);

    private static Task<AiTextGenerationResult> Dispatch(
        OpenAiTextGenerator generator,
        PlanWorld world) =>
        generator.GenerateAsync(
            OpenAiTestHarness.SimpleRequest(),
            AiUsageContext.For(world.Tenant.Id, AiUsagePurpose.ConversationGeneration));

    /// <summary>
    /// The real adapter and the real ledger, over the test database.
    /// </summary>
    /// <remarks>
    /// The ledger opens its own scope per operation, so it needs a container
    /// rather than a context. Both the entitlement service and the clock come
    /// from it, which is what puts the quota check inside the ledger's own
    /// transaction.
    /// </remarks>
    private OpenAiTextGenerator Generator(
        PlanWorld world,
        CannedOpenAiTransport transport,
        DateTimeOffset? now = null)
    {
        _ = world;

        ServiceCollection services = new();

        services.AddDbContext<OmniAiDbContext>(options =>
            options.UseNpgsql(database.ConnectionString));

        services.AddScoped<IWorkspaceEntitlementService, WorkspaceEntitlementService>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now ?? Noon));

        ServiceProvider provider = services.BuildServiceProvider();

        AiProviderUsageLedger ledger = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            FixedPricing.None,
            new FixedTimeProvider(now ?? Noon));

        OpenAiOptions options = new();

        return new OpenAiTextGenerator(
            OpenAiClientFactory.Create(
                OpenAiTestHarness.DummyApiKey,
                options,
                new System.ClientModel.Primitives.HttpClientPipelineTransport(
                    new HttpClient(transport))),
            options,
            ledger,
            NullLogger<OpenAiTextGenerator>.Instance);
    }

    private static async Task<Exception?[]> Outcomes(params Task[] attempts)
    {
        Exception?[] outcomes = new Exception?[attempts.Length];

        for (int index = 0; index < attempts.Length; index++)
        {
            try
            {
                await attempts[index];
            }
            catch (Exception failure)
            {
                outcomes[index] = failure;
            }
        }

        return outcomes;
    }
}
