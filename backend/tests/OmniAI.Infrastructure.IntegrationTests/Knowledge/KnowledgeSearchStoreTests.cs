using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// Retrieval against real pgvector: what it ranks, and what it will not return.
/// </summary>
/// <remarks>
/// <strong>Nothing here is simulated.</strong> Real PostgreSQL 17, the real
/// <c>vector</c> extension, the real HNSW index, the real cosine operator, and
/// the real query the runtime uses. The vectors are chosen by the test so that
/// what is being asserted is the database's arithmetic and the query's filters,
/// rather than a model's opinion about which passage is relevant.
/// </remarks>
public sealed class KnowledgeSearchStoreTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private readonly KnowledgeWorld _world = new(database);

    private async Task<IReadOnlyList<KnowledgeMatch>> SearchAsync(
        TenantId workspace,
        AiAgentId agent,
        float[] query,
        int limit = 10,
        EmbeddingProfileId? profile = null)
    {
        await using OmniAiDbContext context = _world.Context();

        return await KnowledgeWorld.Search(context).SearchAsync(
            workspace,
            agent,
            profile ?? KnowledgeWorld.Profile,
            EmbeddingVector.Create(query, KnowledgeWorld.Dimensions),
            limit);
    }

    private async Task<bool> EligibleAsync(TenantId workspace, AiAgentId agent)
    {
        await using OmniAiDbContext context = _world.Context();

        return await KnowledgeWorld.Search(context)
            .HasEligibleChunksAsync(workspace, agent, KnowledgeWorld.Profile);
    }

    // ------------------------------------------------ connection lifetime (Phase 34)

    [Fact]
    public async Task A_search_gives_the_request_connection_back_when_it_is_done()
    {
        // Found under load: the search opened the request's connection directly,
        // EF never closed it, and every customer turn then held it through the
        // whole provider call -- one extra pooled connection per turn in flight.
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);
        float[] vector = KnowledgeWorld.Axis(1);

        await _world.IndexAsync(document.Value.Id, workspace, document.Version, ("Passage.", vector));
        await _world.AssignAsync(agent, document.Value.Id);

        await using OmniAiDbContext context = _world.Context();
        KnowledgeSearchStore store = KnowledgeWorld.Search(context);

        Assert.True(await store.HasEligibleChunksAsync(workspace, agent.Id, KnowledgeWorld.Profile));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);

        Assert.Single(await store.SearchAsync(
            workspace,
            agent.Id,
            KnowledgeWorld.Profile,
            EmbeddingVector.Create(vector, KnowledgeWorld.Dimensions),
            5));
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task A_search_inside_a_transaction_uses_it_and_leaves_it_open()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);

        await using OmniAiDbContext context = _world.Context();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync();

        Assert.False(await KnowledgeWorld.Search(context)
            .HasEligibleChunksAsync(workspace, agent.Id, KnowledgeWorld.Profile));

        Assert.Equal(System.Data.ConnectionState.Open, context.Database.GetDbConnection().State);
        Assert.Same(transaction, context.Database.CurrentTransaction);

        await transaction.CommitAsync();
    }

    // ----------------------------------------------------------- round trip

    [Fact]
    public async Task A_vector_survives_a_round_trip_through_the_database()
    {
        // Written as a parameter cast to vector, read back through the distance
        // operator. A vector compared against itself has a cosine distance of
        // zero, which is only true if every one of the 1,536 components came back
        // exactly as it went in.
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        float[] vector = KnowledgeWorld.Between(3, 17, 0.37f);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("The passage.", vector));

        await _world.AssignAsync(agent, document.Value.Id);

        KnowledgeMatch only = Assert.Single(await SearchAsync(workspace, agent.Id, vector));

        Assert.Equal(0d, only.Distance, precision: 6);
        Assert.Equal("The passage.", only.Text);
    }

    // -------------------------------------------------------------- ordering

    [Fact]
    public async Task Results_come_back_in_real_cosine_order()
    {
        // Three chunks at known angles from the query. The nearest is the one
        // pointing the same way; the furthest is orthogonal to it. Nothing about
        // this ordering is arranged by the platform -- it is what pgvector
        // computes.
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("orthogonal", KnowledgeWorld.Axis(1)),
            ("near", KnowledgeWorld.Between(0, 1, 0.95f)),
            ("exact", KnowledgeWorld.Axis(0)));

        await _world.AssignAsync(agent, document.Value.Id);

        IReadOnlyList<KnowledgeMatch> matches =
            await SearchAsync(workspace, agent.Id, KnowledgeWorld.Axis(0));

        Assert.Equal(["exact", "near", "orthogonal"], matches.Select(match => match.Text));

        Assert.Equal(
            matches.Select(match => match.Distance).Order(),
            matches.Select(match => match.Distance));
    }

    [Fact]
    public async Task Only_the_requested_number_of_results_comes_back()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("a", KnowledgeWorld.Axis(0)),
            ("b", KnowledgeWorld.Axis(1)),
            ("c", KnowledgeWorld.Axis(2)),
            ("d", KnowledgeWorld.Axis(3)),
            ("e", KnowledgeWorld.Axis(4)));

        await _world.AssignAsync(agent, document.Value.Id);

        Assert.Equal(
            2,
            (await SearchAsync(workspace, agent.Id, KnowledgeWorld.Axis(0), limit: 2)).Count);
    }

    // ------------------------------------------------------------- isolation

    [Fact]
    public async Task Another_workspace_identical_vector_is_never_returned()
    {
        // The sharpest possible arrangement: two workspaces holding the exact same
        // vector, so similarity cannot separate them and only the workspace
        // predicate can. Businesses in one industry writing similar policies is
        // the normal case, not the unlucky one.
        float[] shared = KnowledgeWorld.Axis(0);

        TenantId mine = await _world.WorkspaceAsync("Mine");
        AiAgent myAgent = await _world.AgentAsync(mine);
        Versioned<KnowledgeDocument> myDocument = await _world.DocumentAsync(mine, content: "Mine.");

        await _world.IndexAsync(myDocument.Value.Id, mine, myDocument.Version, ("mine", shared));
        await _world.AssignAsync(myAgent, myDocument.Value.Id);

        TenantId theirs = await _world.WorkspaceAsync("Theirs");
        AiAgent theirAgent = await _world.AgentAsync(theirs);
        Versioned<KnowledgeDocument> theirDocument =
            await _world.DocumentAsync(theirs, content: "Theirs.");

        await _world.IndexAsync(
            theirDocument.Value.Id,
            theirs,
            theirDocument.Version,
            ("theirs", shared));

        await _world.AssignAsync(theirAgent, theirDocument.Value.Id);

        KnowledgeMatch only = Assert.Single(await SearchAsync(mine, myAgent.Id, shared));

        Assert.Equal("mine", only.Text);
    }

    [Fact]
    public async Task Another_agent_in_the_same_workspace_gets_nothing_it_was_not_given()
    {
        // A workspace can hold a public returns policy and an internal escalation
        // note side by side. Assignment is what keeps the second away from the
        // agent answering the public.
        TenantId workspace = await _world.WorkspaceAsync();

        AiAgent support = await _world.AgentAsync(workspace, "Support");
        AiAgent sales = await _world.AgentAsync(workspace, "Sales");

        Versioned<KnowledgeDocument> internalNote =
            await _world.DocumentAsync(workspace, "Escalation", "Internal only.");

        await _world.IndexAsync(
            internalNote.Value.Id,
            workspace,
            internalNote.Version,
            ("internal", KnowledgeWorld.Axis(0)));

        await _world.AssignAsync(sales, internalNote.Value.Id);

        Assert.Empty(await SearchAsync(workspace, support.Id, KnowledgeWorld.Axis(0)));
        Assert.Single(await SearchAsync(workspace, sales.Id, KnowledgeWorld.Axis(0)));
    }

    [Fact]
    public async Task An_unassigned_document_is_never_returned()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("orphan", KnowledgeWorld.Axis(0)));

        Assert.Empty(await SearchAsync(workspace, agent.Id, KnowledgeWorld.Axis(0)));
        Assert.False(await EligibleAsync(workspace, agent.Id));
    }

    // ------------------------------------------------------------ eligibility

    [Fact]
    public async Task A_disabled_document_stops_answering_immediately()
    {
        // The control somebody reaches for when a policy has just changed. It has
        // to take effect on the next question, not after a rebuild.
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        ConcurrencyToken version = await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("live", KnowledgeWorld.Axis(0)));

        await _world.AssignAsync(agent, document.Value.Id);

        Assert.Single(await SearchAsync(workspace, agent.Id, KnowledgeWorld.Axis(0)));

        await using (OmniAiDbContext context = _world.Context())
        {
            KnowledgeDocumentRepository documents =
                new(context, KnowledgeWorld.Embeddings());

            Versioned<KnowledgeDocument> stored =
                (await documents.FindAsync(workspace, document.Value.Id))!;

            stored.Value.Disable(DateTimeOffset.UtcNow);

            await documents.UpdateAsync(stored.Value, version);
        }

        Assert.Empty(await SearchAsync(workspace, agent.Id, KnowledgeWorld.Axis(0)));
        Assert.False(await EligibleAsync(workspace, agent.Id));
    }

    [Fact]
    public async Task An_edited_document_stops_answering_until_it_is_indexed_again()
    {
        // The chunks are still there and still perfectly good vectors. They
        // describe text nobody can see any more, and the fingerprint comparison in
        // the query is what notices -- no status column, no background job, no
        // cache to invalidate.
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        ConcurrencyToken version = await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("live", KnowledgeWorld.Axis(0)));

        await _world.AssignAsync(agent, document.Value.Id);

        Assert.Single(await SearchAsync(workspace, agent.Id, KnowledgeWorld.Axis(0)));

        await using (OmniAiDbContext context = _world.Context())
        {
            KnowledgeDocumentRepository documents =
                new(context, KnowledgeWorld.Embeddings());

            Versioned<KnowledgeDocument> stored =
                (await documents.FindAsync(workspace, document.Value.Id))!;

            stored.Value.Revise(
                stored.Value.Title,
                KnowledgeContent.Create("Something completely different now."),
                DateTimeOffset.UtcNow);

            await documents.UpdateAsync(stored.Value, version);
        }

        Assert.Empty(await SearchAsync(workspace, agent.Id, KnowledgeWorld.Axis(0)));
        Assert.False(await EligibleAsync(workspace, agent.Id));

        // And the chunks were not deleted: they are the honest record of what was
        // indexed, they are simply unreachable.
        Assert.Equal(1L, await _world.ChunkCountAsync(document.Value.Id));
    }

    [Fact]
    public async Task An_index_made_under_another_profile_is_never_compared_against()
    {
        // Distance between vectors from two different models is a well-formed
        // number and a meaningless one. Without this filter the change would be
        // invisible: retrieval would keep working and keep returning confidently
        // ranked nonsense.
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("live", KnowledgeWorld.Axis(0)));

        await _world.AssignAsync(agent, document.Value.Id);

        IReadOnlyList<KnowledgeMatch> matches = await SearchAsync(
            workspace,
            agent.Id,
            KnowledgeWorld.Axis(0),
            profile: EmbeddingProfileId.Create("test:some-other-model:1536"));

        Assert.Empty(matches);
    }

    [Fact]
    public async Task Eligibility_answers_no_before_anything_is_embedded()
    {
        // The cheap question a live turn asks first. A false answer is what makes
        // an agent with no knowledge cost nothing.
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);

        Assert.False(await EligibleAsync(workspace, agent.Id));
    }

    [Fact]
    public async Task Eligibility_answers_yes_only_when_a_search_would_return_something()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        AiAgent agent = await _world.AgentAsync(workspace);
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await _world.IndexAsync(
            document.Value.Id,
            workspace,
            document.Version,
            ("live", KnowledgeWorld.Axis(0)));

        Assert.False(await EligibleAsync(workspace, agent.Id));

        await _world.AssignAsync(agent, document.Value.Id);

        Assert.True(await EligibleAsync(workspace, agent.Id));
    }
}
