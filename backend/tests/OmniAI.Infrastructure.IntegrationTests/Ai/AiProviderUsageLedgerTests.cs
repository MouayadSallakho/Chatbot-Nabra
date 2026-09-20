using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai.Usage;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Ai.Usage;
using OmniAI.Application.Plans;
using OmniAI.Infrastructure.Plans;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Ai;

/// <summary>
/// The ledger, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// Accounting is only worth anything if it is durable and if settling twice
/// cannot double a figure. Both are properties of the database and the aggregate
/// together, so both are proved here rather than against an in-memory double.
/// </remarks>
public sealed class AiProviderUsageLedgerTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly ModelName Model = ModelName.Create("test-model-x");

    [Fact]
    public async Task Beginning_commits_the_row_before_anything_else_happens()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await Ledger().BeginAsync(
            AiUsageContext.For(tenant.Id, AiUsagePurpose.ConversationGeneration),
            AiProvider.OpenAi,
            AiOperationType.TextGeneration,
            Model);

        AiProviderUsage stored = await LoadAsync(id);

        // Committed, unresolved, and attributed. This row is what makes the
        // provider call safe to make.
        Assert.Equal(AiOperationStatus.Started, stored.Status);
        Assert.Equal(tenant.Id, stored.TenantId);
        Assert.Equal(AiUsagePurpose.ConversationGeneration, stored.Purpose);
        Assert.Equal(Noon, stored.StartedAt);
        Assert.Null(stored.CompletedAt);
    }

    [Fact]
    public async Task Every_field_survives_the_round_trip_including_the_absent_ones()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await BeginAsync(tenant);

        await Ledger().SucceedAsync(
            id,
            ProviderTokenUsage.Create(
                inputTokens: 1_200,
                cachedInputTokens: 800,
                outputTokens: 300,
                reasoningTokens: 120,
                totalTokens: 1_500),
            ModelName.Create("test-model-x-2026-09-01"));

        AiProviderUsage stored = await LoadAsync(id);

        Assert.Equal(AiOperationStatus.Succeeded, stored.Status);
        Assert.Equal(1_200, stored.Usage.InputTokens);
        Assert.Equal(800, stored.Usage.CachedInputTokens);
        Assert.Equal(300, stored.Usage.OutputTokens);
        Assert.Equal(120, stored.Usage.ReasoningTokens);
        Assert.Equal(1_500, stored.Usage.TotalTokens);
        Assert.Equal("test-model-x-2026-09-01", stored.ResolvedModel?.Value);
        Assert.Equal("test-model-x", stored.RequestedModel.Value);
    }

    /// <remarks>
    /// The difference the whole feature rests on: a provider that reported
    /// nothing must come back out of the database as nothing, not as nought.
    /// </remarks>
    [Fact]
    public async Task An_unreported_count_comes_back_null_and_not_zero()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await BeginAsync(tenant);

        await Ledger().SucceedAsync(id, ProviderTokenUsage.Unreported, null);

        AiProviderUsage stored = await LoadAsync(id);

        Assert.Null(stored.Usage.InputTokens);
        Assert.Null(stored.Usage.OutputTokens);
        Assert.Null(stored.Usage.TotalTokens);
        Assert.False(stored.Usage.IsReported);

        // And the database agrees, rather than EF reading a default.
        object? raw = await database.ExecuteScalarAsync(
            $"SELECT input_tokens FROM ai_provider_usages WHERE id = '{id.Value}'");

        Assert.True(raw is null or DBNull);
    }

    [Fact]
    public async Task A_rated_call_stores_its_cost_and_the_rates_it_was_priced_with()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await BeginAsync(tenant);

        await Ledger(Pricing()).SucceedAsync(
            id,
            ProviderTokenUsage.Create(inputTokens: 1_000_000, outputTokens: 1_000_000),
            null);

        AiProviderUsage stored = await LoadAsync(id);

        // 1M at 2.00 plus 1M at 8.00.
        Assert.Equal(10.00m, stored.EstimatedCost);
        Assert.Equal("USD", stored.Currency?.Value);
        Assert.Equal(2.00m, stored.InputRatePerMillion);
        Assert.Equal(8.00m, stored.OutputRatePerMillion);
        Assert.Equal(Noon.AddYears(-1), stored.RatesEffectiveFrom);
    }

    [Fact]
    public async Task A_model_with_no_configured_rate_is_stored_unrated()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await BeginAsync(tenant);

        await Ledger(FixedPricing.None).SucceedAsync(
            id,
            ProviderTokenUsage.Create(inputTokens: 500, outputTokens: 100),
            null);

        AiProviderUsage stored = await LoadAsync(id);

        Assert.Equal(AiOperationStatus.Succeeded, stored.Status);
        Assert.Null(stored.EstimatedCost);
        Assert.Null(stored.Currency);

        // Real tokens, no price. Never a cost of zero.
        Assert.Equal(500, stored.Usage.InputTokens);
    }

    // ----------------------------------------------------------- idempotency

    [Fact]
    public async Task Settling_the_same_operation_twice_writes_once()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await BeginAsync(tenant);

        ProviderTokenUsage reported = ProviderTokenUsage.Create(
            inputTokens: 1_000_000,
            outputTokens: 0);

        await Ledger(Pricing()).SucceedAsync(id, reported, null);
        await Ledger(Pricing()).SucceedAsync(id, reported, null);

        AiProviderUsage stored = await LoadAsync(id);

        // Not four pounds. The second settle found it settled and did nothing.
        Assert.Equal(2.00m, stored.EstimatedCost);

        Assert.Equal(1, await CountAsync(tenant));
    }

    [Fact]
    public async Task Concurrent_settles_of_one_operation_are_safe()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await BeginAsync(tenant);

        ProviderTokenUsage reported = ProviderTokenUsage.Create(inputTokens: 1_000_000);

        // Eight racing finalisations, each with its own context.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Ledger(Pricing()).SucceedAsync(id, reported, null)));

        AiProviderUsage stored = await LoadAsync(id);

        Assert.Equal(AiOperationStatus.Succeeded, stored.Status);
        Assert.Equal(2.00m, stored.EstimatedCost);
        Assert.Equal(1, await CountAsync(tenant));
    }

    [Fact]
    public async Task A_failure_cannot_overwrite_a_success()
    {
        Tenant tenant = await NewTenantAsync();

        AiProviderUsageId id = await BeginAsync(tenant);

        await Ledger().SucceedAsync(id, ProviderTokenUsage.Create(inputTokens: 10), null);
        await Ledger().FailAsync(id);
        await Ledger().MarkUnknownAsync(id);

        Assert.Equal(AiOperationStatus.Succeeded, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Settling_an_operation_that_is_not_there_is_not_an_error()
    {
        // The row is the authority. If it is gone there is nothing to correct,
        // and failing a caller who has already paid the provider helps nobody.
        await Ledger().FailAsync(AiProviderUsageId.New());
        await Ledger().SucceedAsync(
            AiProviderUsageId.New(), ProviderTokenUsage.Unreported, null);
    }

    // -------------------------------------------------------------- isolation

    [Fact]
    public async Task One_workspaces_spend_is_never_another_workspaces()
    {
        Tenant mine = await NewTenantAsync();
        Tenant theirs = await NewTenantAsync();

        await BeginAsync(mine);
        await BeginAsync(theirs);
        await BeginAsync(theirs);

        Assert.Equal(1, await CountAsync(mine));
        Assert.Equal(2, await CountAsync(theirs));
    }

    private async Task<AiProviderUsageId> BeginAsync(Tenant tenant) =>
        await Ledger().BeginAsync(
            AiUsageContext.For(tenant.Id, AiUsagePurpose.ConversationGeneration),
            AiProvider.OpenAi,
            AiOperationType.TextGeneration,
            Model);

    /// <summary>
    /// A ledger over the test database.
    /// </summary>
    /// <remarks>
    /// Built with a real scope factory, because the ledger opening its own scope
    /// per operation is the behaviour that keeps accounting independent of the
    /// caller's unit of work — and therefore worth exercising rather than
    /// bypassing.
    /// </remarks>
    private AiProviderUsageLedger Ledger(IAiProviderPricing? pricing = null)
    {
        ServiceCollection services = new();

        services.AddDbContext<OmniAiDbContext>(options =>
        {
            options.UseNpgsql(database.ConnectionString);

            // Same interceptor the rest of the suite uses, so a workspace written
            // through this container has a plan just as onboarding would give it
            // one.
            options.AddInterceptors(new AssignsLegacyPlanInterceptor());
        });

        // The real entitlement service, because the monthly quota is enforced
        // inside Begin and a double would prove nothing about that.
        services.AddScoped<IWorkspaceEntitlementService, WorkspaceEntitlementService>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Noon));

        ServiceProvider provider = services.BuildServiceProvider();

        return new AiProviderUsageLedger(
            provider.GetRequiredService<IServiceScopeFactory>(),
            pricing ?? FixedPricing.None,
            new FixedTimeProvider(Noon));
    }

    private static FixedPricing Pricing() => new(AiModelRates.Create(
        AiProvider.OpenAi,
        Model,
        AiOperationType.TextGeneration,
        CurrencyCode.Create("USD"),
        Noon.AddYears(-1),
        inputPerMillion: 2.00m,
        cachedInputPerMillion: 0.50m,
        outputPerMillion: 8.00m));

    private async Task<AiProviderUsage> LoadAsync(AiProviderUsageId id)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.AiProviderUsages.SingleAsync(operation => operation.Id == id);
    }

    private async Task<int> CountAsync(Tenant tenant)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.AiProviderUsages.CountAsync(
            operation => operation.TenantId == tenant.Id);
    }

    private async Task<Tenant> NewTenantAsync()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();

        return tenant;
    }
}
