using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.IntegrationTests.Knowledge;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Reliability;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Reliability;

/// <summary>
/// Phase 33: work a crashed process left in flight is made truthful, once.
/// </summary>
/// <remarks>
/// Against real PostgreSQL 17 + pgvector, because every property asserted here
/// — conditional settlement, SKIP LOCKED batching, the alert written in the same
/// statement, the xmin guard — belongs to the database and not to C#.
/// </remarks>
public sealed class StaleWorkReconciliationTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly KnowledgeWorld _world = new(database);

    // ---- Provider usage ---------------------------------------------------

    [Fact]
    public async Task A_stale_started_operation_becomes_unknown_without_invented_tokens_or_cost()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        AiProviderUsageId stale = await StartedUsageAsync(tenant, Noon.AddHours(-2));

        await using OmniAiDbContext context = database.CreateContext();

        await new ProviderUsageReconciler(context).ReconcileAsync(Noon.AddMinutes(-15), Noon, 100);

        AiProviderUsage stored = await LoadUsageAsync(stale);

        Assert.Equal(AiOperationStatus.Unknown, stored.Status);
        Assert.Equal(Noon, stored.CompletedAt);

        // Nothing fabricated: the provider never said what it used or cost.
        Assert.False(stored.Usage.IsReported);
        Assert.Null(stored.Usage.TotalTokens);
        Assert.Null(await database.ExecuteScalarAsync(
            $"SELECT estimated_cost FROM ai_provider_usages WHERE id = '{stale.Value}' AND estimated_cost IS NOT NULL"));
    }

    [Fact]
    public async Task An_operation_younger_than_the_threshold_is_left_running()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        AiProviderUsageId running = await StartedUsageAsync(tenant, Noon.AddMinutes(-2));

        await using OmniAiDbContext context = database.CreateContext();

        await new ProviderUsageReconciler(context).ReconcileAsync(Noon.AddMinutes(-15), Noon, 100);

        Assert.Equal(AiOperationStatus.Started, (await LoadUsageAsync(running)).Status);
    }

    [Fact]
    public async Task A_settled_operation_never_moves_and_reconciling_again_changes_nothing()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        AiProviderUsageId stale = await StartedUsageAsync(tenant, Noon.AddHours(-3));

        await using (OmniAiDbContext first = database.CreateContext())
        {
            await new ProviderUsageReconciler(first).ReconcileAsync(Noon.AddMinutes(-15), Noon, 100);
        }

        await using (OmniAiDbContext second = database.CreateContext())
        {
            await new ProviderUsageReconciler(second).ReconcileAsync(Noon.AddMinutes(-15), Noon.AddHours(1), 100);
        }

        // The second pass did not touch it: completed_at is still the first pass's.
        Assert.Equal(Noon, (await LoadUsageAsync(stale)).CompletedAt);
    }

    [Fact]
    public async Task One_pass_settles_at_most_one_batch()
    {
        TenantId tenant = await _world.WorkspaceAsync();

        for (int i = 0; i < 5; i++)
        {
            await StartedUsageAsync(tenant, Noon.AddHours(-5).AddMinutes(i));
        }

        await using OmniAiDbContext context = database.CreateContext();

        int settled = await new ProviderUsageReconciler(context).ReconcileAsync(Noon.AddMinutes(-15), Noon, 2);

        Assert.Equal(2, settled);
    }

    [Fact]
    public async Task A_late_settlement_cannot_overwrite_what_the_reconciler_recorded()
    {
        // The race the xmin token closes: a finaliser loaded the row while it
        // was Started, the reconciler settled it, then the finaliser saved.
        TenantId tenant = await _world.WorkspaceAsync();
        AiProviderUsageId id = await StartedUsageAsync(tenant, Noon.AddHours(-2));

        await using OmniAiDbContext finaliser = database.CreateContext();

        // 1. Loaded while Started, at version A.
        AiProviderUsage loaded = await finaliser.AiProviderUsages.SingleAsync(usage => usage.Id == id);
        uint versionA = finaliser.Entry(loaded).Property<uint>("xmin").CurrentValue;

        Assert.Equal(versionA, await SystemXminAsync(id));

        // 2. The reconciler settles it Unknown; PostgreSQL moves xmin past A.
        await using (OmniAiDbContext reconciling = database.CreateContext())
        {
            await new ProviderUsageReconciler(reconciling).ReconcileAsync(Noon.AddMinutes(-15), Noon, 100);
        }

        Assert.NotEqual(versionA, await SystemXminAsync(id));

        // 3. The stale finaliser, still holding version A, tries a terminal settlement.
        Assert.True(loaded.Succeed(ProviderTokenUsage.Create(inputTokens: 10, outputTokens: 5), null, null, Noon));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => finaliser.SaveChangesAsync());

        // 4. Unknown stands, with nothing invented on it.
        AiProviderUsage stored = await LoadUsageAsync(id);

        Assert.Equal(AiOperationStatus.Unknown, stored.Status);
        Assert.False(stored.Usage.IsReported);
    }

    [Fact]
    public async Task An_uncontested_settlement_still_succeeds_and_advances_the_version()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        AiProviderUsageId id = await StartedUsageAsync(tenant, Noon.AddMinutes(-1));

        await using OmniAiDbContext finaliser = database.CreateContext();

        AiProviderUsage loaded = await finaliser.AiProviderUsages.SingleAsync(usage => usage.Id == id);
        uint before = finaliser.Entry(loaded).Property<uint>("xmin").CurrentValue;

        Assert.True(loaded.Succeed(ProviderTokenUsage.Create(inputTokens: 10, outputTokens: 5), null, null, Noon));
        await finaliser.SaveChangesAsync();

        AiProviderUsage stored = await LoadUsageAsync(id);

        Assert.Equal(AiOperationStatus.Succeeded, stored.Status);
        Assert.Equal(10, stored.Usage.InputTokens);

        // EF read the new system xmin back after the update, so a later change
        // through the same context is checked against the right version.
        uint after = finaliser.Entry(loaded).Property<uint>("xmin").CurrentValue;

        Assert.NotEqual(before, after);
        Assert.Equal(after, await SystemXminAsync(id));
    }

    [Fact]
    public async Task Unknown_still_counts_toward_the_quota_exactly_as_before()
    {
        // Quota semantics are unchanged: Started and Unknown are both counted, so
        // reconciling never gives a workspace budget back it may have spent.
        TenantId tenant = await _world.WorkspaceAsync();
        await StartedUsageAsync(tenant, Noon.AddHours(-2));

        object? before = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM ai_provider_usages WHERE tenant_id = '{tenant.Value}' AND status IN ('Started', 'Unknown')");

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await new ProviderUsageReconciler(context).ReconcileAsync(Noon.AddMinutes(-15), Noon, 100);
        }

        object? after = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM ai_provider_usages WHERE tenant_id = '{tenant.Value}' AND status IN ('Started', 'Unknown')");

        Assert.Equal(before, after);
    }

    // ---- Knowledge indexing attempts --------------------------------------

    [Fact]
    public async Task An_explicit_failure_is_recorded_with_exactly_one_alert_in_the_same_statement()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);
        KnowledgeIndexAttempt attempt = await StartAttemptAsync(tenant, document.Value.Id, Noon);

        await using OmniAiDbContext context = database.CreateContext();
        KnowledgeIndexAttemptStore store = new(context);

        Assert.True(await store.SettleAsync(
            attempt.Id, KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.ProviderTimeout, alert: true, Noon));

        // Settling again — a duplicate finaliser, or the reconciler — is a no-op.
        Assert.False(await store.SettleAsync(
            attempt.Id, KnowledgeIndexAttemptStatus.Unknown, KnowledgeIndexAttemptFailures.Interrupted, alert: true, Noon));

        Assert.Equal("Failed|provider_timeout", await AttemptStateAsync(attempt.Id));
        Assert.Equal(1L, await AlertCountAsync(tenant, document.Value.Id));
    }

    [Fact]
    public async Task An_expected_refusal_is_recorded_without_an_alert()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);
        KnowledgeIndexAttempt attempt = await StartAttemptAsync(tenant, document.Value.Id, Noon);

        await using OmniAiDbContext context = database.CreateContext();

        await new KnowledgeIndexAttemptStore(context).SettleAsync(
            attempt.Id, KnowledgeIndexAttemptStatus.Failed, KnowledgeIndexAttemptFailures.QuotaExhausted, alert: false, Noon);

        Assert.Equal(0L, await AlertCountAsync(tenant, document.Value.Id));
    }

    [Fact]
    public async Task An_abandoned_attempt_whose_index_was_never_written_becomes_unknown_with_one_alert()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);
        KnowledgeIndexAttempt attempt = await StartAttemptAsync(tenant, document.Value.Id, DateTimeOffset.UtcNow.AddHours(-2));

        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int pass = 0; pass < 2; pass++)
        {
            await using OmniAiDbContext context = database.CreateContext();

            await new KnowledgeAttemptReconciler(context).ReconcileAsync(now.AddMinutes(-30), now, 100);
        }

        Assert.Equal("Unknown|interrupted", await AttemptStateAsync(attempt.Id));

        // Two passes, one alert: the occurrence key is the attempt id.
        Assert.Equal(1L, await AlertCountAsync(tenant, document.Value.Id));
    }

    [Fact]
    public async Task An_abandoned_attempt_whose_index_was_written_is_recorded_as_succeeded_without_an_alert()
    {
        // The process died after committing the index but before recording the
        // outcome. The index is the evidence; nothing is alerted or re-embedded.
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);
        KnowledgeIndexAttempt attempt = await StartAttemptAsync(tenant, document.Value.Id, DateTimeOffset.UtcNow.AddHours(-2));

        await _world.IndexAsync(document.Value.Id, tenant, document.Version, ("chunk", KnowledgeWorld.Axis(0)));

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using OmniAiDbContext context = database.CreateContext();

        await new KnowledgeAttemptReconciler(context).ReconcileAsync(now.AddMinutes(-30), now, 100);

        Assert.Equal("Succeeded|", await AttemptStateAsync(attempt.Id));
        Assert.Equal(0L, await AlertCountAsync(tenant, document.Value.Id));
    }

    [Fact]
    public async Task One_knowledge_pass_settles_at_most_one_batch()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);

        for (int i = 0; i < 4; i++)
        {
            await StartAttemptAsync(tenant, document.Value.Id, DateTimeOffset.UtcNow.AddHours(-3).AddMinutes(i));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using OmniAiDbContext context = database.CreateContext();

        Assert.Equal(2, await new KnowledgeAttemptReconciler(context).ReconcileAsync(now.AddMinutes(-30), now, 2));
    }

    [Fact]
    public async Task An_attempt_still_inside_the_threshold_is_left_alone()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);
        KnowledgeIndexAttempt attempt = await StartAttemptAsync(tenant, document.Value.Id, DateTimeOffset.UtcNow.AddMinutes(-1));

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using OmniAiDbContext context = database.CreateContext();

        await new KnowledgeAttemptReconciler(context).ReconcileAsync(now.AddMinutes(-30), now, 100);

        Assert.Equal("Started|", await AttemptStateAsync(attempt.Id));
    }

    [Fact]
    public async Task The_database_refuses_a_settled_attempt_without_a_completion_time()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);
        KnowledgeIndexAttempt attempt = await StartAttemptAsync(tenant, document.Value.Id, Noon);

        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteScalarAsync(
            $"UPDATE knowledge_index_attempts SET status = 'Failed' WHERE id = '{attempt.Id}'"));
    }

    [Fact]
    public async Task An_attempt_cannot_name_another_workspace_s_document()
    {
        TenantId owner = await _world.WorkspaceAsync();
        TenantId other = await _world.WorkspaceAsync("Other Co");
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(owner);

        await Assert.ThrowsAnyAsync<Exception>(() => StartAttemptAsync(other, document.Value.Id, Noon));
    }

    // ---- Stale thresholds --------------------------------------------------

    [Fact]
    public void Startup_refuses_thresholds_inside_the_real_request_bounds()
    {
        TimeSpan timeout = TimeSpan.FromSeconds(30);
        TimeSpan indexing = TimeSpan.FromMinutes(6.5);

        new ReliabilityOptions().Validate(timeout, indexing);

        Assert.Throws<InvalidOperationException>(() =>
            new ReliabilityOptions { ProviderOperationStaleAfter = TimeSpan.FromMinutes(5) }.Validate(timeout, indexing));
        Assert.Throws<InvalidOperationException>(() =>
            new ReliabilityOptions { KnowledgeAttemptStaleAfter = TimeSpan.FromMinutes(10) }.Validate(timeout, indexing));
        Assert.Throws<InvalidOperationException>(() =>
            new ReliabilityOptions { BatchSize = 0 }.Validate(timeout, indexing));

        // A longer provider timeout moves the floor with it.
        Assert.Throws<InvalidOperationException>(() =>
            new ReliabilityOptions().Validate(TimeSpan.FromMinutes(6), indexing));
    }

    // ---- Helpers ----------------------------------------------------------

    private async Task<AiProviderUsageId> StartedUsageAsync(TenantId tenant, DateTimeOffset startedAt)
    {
        AiProviderUsage usage = AiProviderUsage.Begin(
            tenant,
            AiProvider.OpenAi,
            AiOperationType.TextGeneration,
            AiUsagePurpose.ConversationGeneration,
            ModelName.Create("test-model-x"),
            startedAt);

        await using OmniAiDbContext context = database.CreateContext();

        context.AiProviderUsages.Add(usage);
        await context.SaveChangesAsync();

        return usage.Id;
    }

    /// <summary>PostgreSQL's own xmin for the row, read directly from the system column.</summary>
    private async Task<uint> SystemXminAsync(AiProviderUsageId id) =>
        Convert.ToUInt32(
            await database.ExecuteScalarAsync(
                $"SELECT xmin::text::bigint FROM ai_provider_usages WHERE id = '{id.Value}'"),
            CultureInfo.InvariantCulture);

    private async Task<AiProviderUsage> LoadUsageAsync(AiProviderUsageId id)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.AiProviderUsages.AsNoTracking().SingleAsync(usage => usage.Id == id);
    }

    private async Task<KnowledgeIndexAttempt> StartAttemptAsync(TenantId tenant, KnowledgeDocumentId document, DateTimeOffset startedAt)
    {
        KnowledgeIndexAttempt attempt = KnowledgeIndexAttempt.Start(tenant, document, startedAt);

        await using OmniAiDbContext context = database.CreateContext();

        await new KnowledgeIndexAttemptStore(context).StartAsync(attempt);

        return attempt;
    }

    private async Task<string> AttemptStateAsync(Guid attemptId) =>
        (string)(await database.ExecuteScalarAsync(
            $"SELECT status || '|' || coalesce(failure_code, '') FROM knowledge_index_attempts WHERE id = '{attemptId}'"))!;

    private async Task<long> AlertCountAsync(TenantId tenant, KnowledgeDocumentId document) =>
        Convert.ToInt64(
            await database.ExecuteScalarAsync(
                "SELECT count(*) FROM notification_occurrences "
                + $"WHERE tenant_id = '{tenant.Value}' AND kind = 'KnowledgeIndexingFailed' "
                + $"AND source_id = '{document.Value}'"),
            CultureInfo.InvariantCulture);
}
