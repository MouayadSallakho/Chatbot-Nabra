using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Reports;
using OmniAI.Domain.Reports;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Reports;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Reports;

/// <summary>
/// Report executions against a real PostgreSQL: idempotency, claiming, settlement.
/// </summary>
public sealed class ReportExecutionStoreTests(PostgresTestDatabase database) : IClassFixture<PostgresTestDatabase>
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task An_execution_round_trips_and_is_invisible_to_another_workspace()
    {
        ReportWorld mine = await ReportWorld.CreateAsync(database);
        ReportWorld theirs = await ReportWorld.CreateAsync(database, "Rival");

        ReportExecution execution = await mine.PendingAsync(ReportType.ProviderCost, ReportFormat.Pdf);

        await using OmniAiDbContext context = database.CreateContext();
        ReportExecutionStore store = new(context);

        ReportExecutionRecord? found = await store.FindAsync(mine.Tenant, execution.Id);

        Assert.NotNull(found);
        Assert.Equal(ReportType.ProviderCost, found.Execution.Type);
        Assert.Equal(ReportFormat.Pdf, found.Execution.Format);
        Assert.Equal(ReportWorld.September, found.Execution.Period);
        Assert.Equal(ReportExecutionStatus.Pending, found.Execution.Status);

        // Another workspace addressing the same id finds nothing at all.
        Assert.Null(await store.FindAsync(theirs.Tenant, execution.Id));
        Assert.Empty((await store.ListAsync(theirs.Tenant, 1, 20)).Items);
    }

    [Fact]
    public async Task The_same_client_request_is_one_execution_however_many_times_it_arrives()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        Guid clientRequestId = Guid.NewGuid();

        // Ten concurrent submissions of one click, each in its own context, as ten
        // requests would be.
        (ReportExecution Execution, bool Created)[] results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(async _ =>
            {
                await using OmniAiDbContext context = database.CreateContext();

                return await new ReportExecutionStore(context).AddOrGetAsync(ReportExecution.Request(
                    world.Tenant, ReportType.ChannelUsage, ReportFormat.Csv, ReportWorld.September,
                    world.Owner, clientRequestId, DateTimeOffset.UtcNow));
            }));

        Assert.Single(results, result => result.Created);
        Assert.Single(results.Select(result => result.Execution.Id).Distinct());

        await using OmniAiDbContext check = database.CreateContext();
        Assert.Equal(1, await check.ReportExecutions.CountAsync(execution => execution.ClientRequestId == clientRequestId));
    }

    [Fact]
    public async Task A_new_client_request_id_is_a_new_execution()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);

        ReportExecution first = await world.PendingAsync();
        ReportExecution second = await world.PendingAsync();

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Two_workers_claiming_at_once_take_disjoint_executions()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);

        // Earlier than anything else in this database, so these are what both
        // workers reach for first.
        DateTimeOffset queued = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        List<ReportExecutionId> ids = [];

        for (int index = 0; index < 8; index++)
        {
            ids.Add((await world.PendingAsync(at: queued.AddSeconds(index))).Id);
        }

        IReadOnlyList<ClaimedReportExecution>[] claims = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(async _ =>
            {
                await using OmniAiDbContext context = database.CreateContext();

                return await new ReportExecutionStore(context).ClaimAsync(8, 3, Lease, DateTimeOffset.UtcNow);
            }));

        List<ReportExecutionId> claimed =
        [
            .. claims.SelectMany(batch => batch).Select(execution => execution.Id).Where(ids.Contains),
        ];

        // Exactly once each, across both workers.
        Assert.Equal(ids.Count, claimed.Count);
        Assert.Equal(ids.Count, claimed.Distinct().Count());
        Assert.All(claims.SelectMany(batch => batch).Where(execution => ids.Contains(execution.Id)), execution => Assert.Equal(1, execution.Attempts));
    }

    [Fact]
    public async Task A_lapsed_lease_is_taken_over_and_the_old_claim_can_settle_nothing()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportExecution execution = await world.PendingAsync(at: new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ClaimedReportExecution first = await ClaimAsync(execution.Id, now);

        // Within the lease, nobody else may take it.
        await using (OmniAiDbContext context = database.CreateContext())
        {
            Assert.DoesNotContain(
                await new ReportExecutionStore(context).ClaimAsync(50, 3, Lease, now.AddMinutes(1)),
                claimed => claimed.Id == execution.Id);
        }

        ClaimedReportExecution second = await ClaimAsync(execution.Id, now + Lease + TimeSpan.FromSeconds(1));

        Assert.NotEqual(first.ClaimToken, second.ClaimToken);
        Assert.Equal(2, second.Attempts);

        await using OmniAiDbContext settling = database.CreateContext();
        ReportExecutionStore store = new(settling);

        Assert.False(await store.FailAsync(execution.Id, first.ClaimToken, ReportFailures.GenerationFailed, now));
        Assert.False(await store.CompleteAsync(execution.Id, first.ClaimToken, Artifact(world, execution), now));
        Assert.True(await store.CompleteAsync(execution.Id, second.ClaimToken, Artifact(world, execution), now));

        ReportExecutionRecord? done = await store.FindAsync(world.Tenant, execution.Id);
        Assert.Equal(ReportExecutionStatus.Completed, done!.Execution.Status);
        Assert.Equal(1234, done.ByteLength);
    }

    [Fact]
    public async Task An_execution_out_of_attempts_is_failed_rather_than_claimed_again()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportExecution execution = await world.PendingAsync(at: new DateTimeOffset(2019, 1, 2, 0, 0, 0, TimeSpan.Zero));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await ClaimAsync(execution.Id, now, maxAttempts: 1);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Assert.DoesNotContain(
                await new ReportExecutionStore(context).ClaimAsync(50, 1, Lease, now + Lease + Lease),
                claimed => claimed.Id == execution.Id);
        }

        await using OmniAiDbContext check = database.CreateContext();
        ReportExecutionRecord? failed = await new ReportExecutionStore(check).FindAsync(world.Tenant, execution.Id);

        Assert.Equal(ReportExecutionStatus.Failed, failed!.Execution.Status);
        Assert.Equal(ReportFailures.GenerationFailed, failed.Execution.FailureCode);
    }

    [Fact]
    public async Task A_released_execution_is_pending_again_and_keeps_its_attempt_count()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportExecution execution = await world.PendingAsync(at: new DateTimeOffset(2019, 1, 3, 0, 0, 0, TimeSpan.Zero));

        ClaimedReportExecution claim = await ClaimAsync(execution.Id, DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();
        ReportExecutionStore store = new(context);

        Assert.True(await store.ReleaseAsync(execution.Id, claim.ClaimToken));

        ReportExecutionRecord? record = await store.FindAsync(world.Tenant, execution.Id);
        Assert.Equal(ReportExecutionStatus.Pending, record!.Execution.Status);
        Assert.Equal(1, record.Execution.Attempts);
    }

    [Fact]
    public async Task Artifact_metadata_is_stored_once_per_execution_and_only_for_its_workspace()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        ReportWorld other = await ReportWorld.CreateAsync(database, "Other");
        ReportExecution execution = await world.PendingAsync(at: new DateTimeOffset(2019, 1, 4, 0, 0, 0, TimeSpan.Zero));

        ClaimedReportExecution claim = await ClaimAsync(execution.Id, DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();
        ReportExecutionStore store = new(context);

        ReportArtifact artifact = Artifact(world, execution);
        Assert.True(await store.CompleteAsync(execution.Id, claim.ClaimToken, artifact, DateTimeOffset.UtcNow));

        ReportArtifact? stored = await store.FindArtifactAsync(world.Tenant, execution.Id);
        Assert.NotNull(stored);
        Assert.Equal(artifact.Sha256, stored.Sha256);
        Assert.Equal(artifact.StorageKey, stored.StorageKey);
        Assert.Equal("text/csv; charset=utf-8", stored.ContentType);

        Assert.Null(await store.FindArtifactAsync(other.Tenant, execution.Id));
    }

    [Fact]
    public async Task Retention_expires_only_what_has_expired()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);

        ReportExecution old = await world.PendingAsync(at: new DateTimeOffset(2019, 1, 5, 0, 0, 0, TimeSpan.Zero));
        ReportExecution fresh = await world.PendingAsync(at: new DateTimeOffset(2019, 1, 5, 0, 0, 1, TimeSpan.Zero));

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            ReportExecutionStore store = new(context);

            IReadOnlyList<ClaimedReportExecution> claims = await store.ClaimAsync(500, 3, Lease, now);
            ClaimedReportExecution oldClaim = claims.Single(claim => claim.Id == old.Id);
            ClaimedReportExecution freshClaim = claims.Single(claim => claim.Id == fresh.Id);

            await store.CompleteAsync(old.Id, oldClaim.ClaimToken, Artifact(world, old, createdAt: now.AddDays(-40)), now);
            await store.CompleteAsync(fresh.Id, freshClaim.ClaimToken, Artifact(world, fresh, createdAt: now), now);
        }

        await using OmniAiDbContext sweeping = database.CreateContext();
        ReportExecutionStore sweeper = new(sweeping);

        IReadOnlyList<ReportArtifact> expired = [.. (await sweeper.ListExpiredArtifactsAsync(now, 500)).Where(artifact => artifact.TenantId == world.Tenant)];

        Assert.Equal([old.Id], expired.Select(artifact => artifact.ExecutionId));

        await sweeper.MarkExpiredAsync(expired[0], now);
        await sweeper.MarkExpiredAsync(expired[0], now);

        ReportExecutionRecord? oldRecord = await sweeper.FindAsync(world.Tenant, old.Id);
        ReportExecutionRecord? freshRecord = await sweeper.FindAsync(world.Tenant, fresh.Id);

        Assert.Equal(ReportExecutionStatus.Expired, oldRecord!.Execution.Status);
        Assert.Null(oldRecord.ByteLength);
        Assert.Null(await sweeper.FindArtifactAsync(world.Tenant, old.Id));

        Assert.Equal(ReportExecutionStatus.Completed, freshRecord!.Execution.Status);
        Assert.NotNull(await sweeper.FindArtifactAsync(world.Tenant, fresh.Id));
    }

    [Fact]
    public async Task The_list_is_newest_first_and_bounded()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(-1);

        for (int index = 0; index < 5; index++)
        {
            await world.PendingAsync(at: start.AddMinutes(index));
        }

        await using OmniAiDbContext context = database.CreateContext();
        (IReadOnlyList<ReportExecutionRecord> items, int total) = await new ReportExecutionStore(context).ListAsync(world.Tenant, 1, 3);

        Assert.Equal(5, total);
        Assert.Equal(3, items.Count);
        Assert.True(items[0].Execution.RequestedAt > items[1].Execution.RequestedAt);
    }

    [Fact]
    public async Task The_report_tables_hold_metadata_only_and_no_physical_row_version()
    {
        // A report's bytes live in the artifact store; nothing here is a blob, and
        // the row version is PostgreSQL's own xmin rather than a column of ours.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(table_name || '.' || column_name || ':' || data_type, ', ' "
            + "ORDER BY table_name, column_name) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name LIKE 'report_%' "
            + "AND (data_type IN ('bytea', 'json', 'jsonb', 'text') OR column_name = 'xmin')");

        Assert.True(columns is null or DBNull, columns?.ToString());
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Claims until this particular execution is taken; others may come along.</summary>
    private async Task<ClaimedReportExecution> ClaimAsync(ReportExecutionId id, DateTimeOffset now, int maxAttempts = 3)
    {
        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyList<ClaimedReportExecution> claimed = await new ReportExecutionStore(context).ClaimAsync(500, maxAttempts, Lease, now);

        return claimed.Single(execution => execution.Id == id);
    }

    private static ReportArtifact Artifact(ReportWorld world, ReportExecution execution, DateTimeOffset? createdAt = null) =>
        ReportArtifact.Create(
            world.Tenant,
            execution.Id,
            execution.Format,
            ReportCatalog.ContentType(execution.Format),
            1234,
            new string('a', 64),
            Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()),
            createdAt ?? DateTimeOffset.UtcNow,
            TimeSpan.FromDays(30));
}
