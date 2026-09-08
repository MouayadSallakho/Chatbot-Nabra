using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// Committing an index: all at once, or not at all.
/// </summary>
public sealed class KnowledgeIndexStoreTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private readonly KnowledgeWorld _world = new(database);

    [Fact]
    public async Task Indexing_writes_every_chunk_in_order()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("first", KnowledgeWorld.Axis(0)),
            ("second", KnowledgeWorld.Axis(1)),
            ("third", KnowledgeWorld.Axis(2)));

        Assert.Equal(
            ["first", "second", "third"],
            await _world.ChunkTextsAsync(document.Value.Id));
    }

    [Fact]
    public async Task Re_indexing_replaces_the_whole_set_rather_than_adding_to_it()
    {
        // Chunks left over from a previous version would answer questions from
        // text that is no longer in the document, and nothing would say so.
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        ConcurrencyToken version = await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("old one", KnowledgeWorld.Axis(0)),
            ("old two", KnowledgeWorld.Axis(1)),
            ("old three", KnowledgeWorld.Axis(2)));

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            version,
            ("new one", KnowledgeWorld.Axis(3)));

        Assert.Equal(["new one"], await _world.ChunkTextsAsync(document.Value.Id));
        Assert.Equal(1L, await _world.ChunkCountAsync(document.Value.Id));
    }

    [Fact]
    public async Task Indexing_advances_the_document_version()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        ConcurrencyToken after = await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("only", KnowledgeWorld.Axis(0)));

        Assert.NotEqual(document.Version, after);
    }

    [Fact]
    public async Task A_stale_index_cannot_overwrite_a_newer_document()
    {
        // The expensive race: the embeddings for the text somebody read take
        // seconds to produce, and the document is edited in that window. Attaching
        // them would leave the index describing a draft nobody can see.
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        ConcurrencyToken stale = document.Version;

        // Somebody else saves while the embeddings are being produced.
        await using (OmniAiDbContext context = _world.Context())
        {
            KnowledgeDocumentRepository documents = new(context, KnowledgeWorld.Embeddings());

            Versioned<KnowledgeDocument> current =
                (await documents.FindAsync(workspace, document.Value.Id))!;

            current.Value.Revise(
                current.Value.Title,
                KnowledgeContent.Create("A newer draft entirely."),
                DateTimeOffset.UtcNow);

            await documents.UpdateAsync(current.Value, current.Version);
        }

        await Assert.ThrowsAsync<KnowledgeDocumentVersionMismatchException>(() =>
            _world.IndexAsync(
                document.Value.Id,
                workspace,
                stale,
                ("from the old draft", KnowledgeWorld.Axis(0))));

        // Nothing partial: no chunk was written at all.
        Assert.Equal(0L, await _world.ChunkCountAsync(document.Value.Id));
    }

    [Fact]
    public async Task A_refused_re_index_leaves_the_previous_chunks_exactly_as_they_were()
    {
        // The transaction rolls back, so the document keeps answering from the
        // index it had rather than being left with nothing.
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        ConcurrencyToken version = await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("good one", KnowledgeWorld.Axis(0)),
            ("good two", KnowledgeWorld.Axis(1)));

        await using (OmniAiDbContext context = _world.Context())
        {
            KnowledgeDocumentRepository documents = new(context, KnowledgeWorld.Embeddings());

            Versioned<KnowledgeDocument> current =
                (await documents.FindAsync(workspace, document.Value.Id))!;

            current.Value.Revise(
                current.Value.Title,
                KnowledgeContent.Create("Edited underneath the indexer."),
                DateTimeOffset.UtcNow);

            await documents.UpdateAsync(current.Value, current.Version);
        }

        await Assert.ThrowsAsync<KnowledgeDocumentVersionMismatchException>(() =>
            _world.IndexAsync(
                document.Value.Id,
                workspace,
                version,
                ("replacement", KnowledgeWorld.Axis(5))));

        Assert.Equal(
            ["good one", "good two"],
            await _world.ChunkTextsAsync(document.Value.Id));
    }

    [Fact]
    public async Task A_chunk_carries_the_workspace_of_its_own_document()
    {
        // Written from the document rather than passed in, so a chunk cannot be
        // filed under a workspace its document does not belong to. The composite
        // foreign key would refuse it; this makes the attempt impossible.
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("only", KnowledgeWorld.Axis(0)));

        object? mismatched = await database.ExecuteScalarAsync(
            $"""
            SELECT count(*) FROM knowledge_chunks c
              JOIN knowledge_documents d ON d.id = c.knowledge_document_id
             WHERE c.knowledge_document_id = '{document.Value.Id}'
               AND c.tenant_id <> d.tenant_id
            """);

        Assert.Equal(0L, mismatched);
    }

    [Fact]
    public async Task Deleting_a_document_removes_its_chunks()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("only", KnowledgeWorld.Axis(0)));

        await using (OmniAiDbContext context = _world.Context())
        {
            await context.KnowledgeDocuments
                .Where(candidate => candidate.Id == document.Value.Id)
                .ExecuteDeleteAsync();
        }

        Assert.Equal(0L, await _world.ChunkCountAsync(document.Value.Id));
    }
}
