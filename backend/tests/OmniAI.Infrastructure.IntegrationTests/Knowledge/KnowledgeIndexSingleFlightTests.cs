using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// Phase 35: one paid indexing attempt per document at a time, enforced by the
/// database.
/// </summary>
/// <remarks>
/// Phase 34 indexed one document four times at once and paid for four embedding
/// batches of one version. The gate is the Started attempt row, which is committed
/// before any embedding request: the partial unique index lets only one exist.
/// </remarks>
public sealed class KnowledgeIndexSingleFlightTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private readonly KnowledgeWorld _world = new(database);

    [Fact]
    public async Task A_second_attempt_for_a_document_being_indexed_is_refused_before_anything_is_sent()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);

        await StartAsync(tenant, document.Value.Id);

        await Assert.ThrowsAsync<KnowledgeIndexingInProgressException>(() => StartAsync(tenant, document.Value.Id));

        Assert.Equal(1L, await StartedCountAsync(document.Value.Id));
    }

    [Fact]
    public async Task Of_several_attempts_started_at_the_same_moment_exactly_one_is_admitted()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);

        Task[] racing = [.. Enumerable.Range(0, 8).Select(_ => StartAsync(tenant, document.Value.Id))];

        try
        {
            await Task.WhenAll(racing);
        }
        catch (KnowledgeIndexingInProgressException)
        {
            // Inspected below, one by one.
        }

        Assert.Equal(1, racing.Count(task => task.IsCompletedSuccessfully));
        Assert.All(
            racing.Where(task => !task.IsCompletedSuccessfully),
            task => Assert.IsType<KnowledgeIndexingInProgressException>(task.Exception!.InnerException));
        Assert.Equal(1L, await StartedCountAsync(document.Value.Id));
    }

    [Fact]
    public async Task Different_documents_index_concurrently()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> first = await _world.DocumentAsync(tenant, "First");
        Versioned<KnowledgeDocument> second = await _world.DocumentAsync(tenant, "Second");

        await Task.WhenAll(StartAsync(tenant, first.Value.Id), StartAsync(tenant, second.Value.Id));

        Assert.Equal(1L, await StartedCountAsync(first.Value.Id));
        Assert.Equal(1L, await StartedCountAsync(second.Value.Id));
    }

    /// <remarks>
    /// Single flight, not single use: once the running attempt settles, however it
    /// ended, the document can be indexed again.
    /// </remarks>
    [Fact]
    public async Task A_settled_attempt_releases_the_document()
    {
        TenantId tenant = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(tenant);

        KnowledgeIndexAttempt failed = await StartAsync(tenant, document.Value.Id);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Assert.True(await new KnowledgeIndexAttemptStore(context).SettleAsync(
                failed.Id,
                KnowledgeIndexAttemptStatus.Failed,
                KnowledgeIndexAttemptFailures.ProviderTimeout,
                alert: false,
                DateTimeOffset.UtcNow));
        }

        KnowledgeIndexAttempt succeeded = await StartAsync(tenant, document.Value.Id);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            Assert.True(await new KnowledgeIndexAttemptStore(context).SucceedAsync(succeeded.Id, DateTimeOffset.UtcNow));
        }

        await StartAsync(tenant, document.Value.Id);

        Assert.Equal(1L, await StartedCountAsync(document.Value.Id));
    }

    private async Task<KnowledgeIndexAttempt> StartAsync(TenantId tenant, KnowledgeDocumentId document)
    {
        KnowledgeIndexAttempt attempt = KnowledgeIndexAttempt.Start(tenant, document, DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();

        await new KnowledgeIndexAttemptStore(context).StartAsync(attempt);

        return attempt;
    }

    private async Task<long> StartedCountAsync(KnowledgeDocumentId document) =>
        Convert.ToInt64(
            await database.ExecuteScalarAsync(
                $"SELECT count(*) FROM knowledge_index_attempts WHERE document_id = '{document.Value}' AND status = 'Started'"),
            System.Globalization.CultureInfo.InvariantCulture);
}
