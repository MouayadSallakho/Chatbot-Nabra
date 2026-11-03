using Microsoft.EntityFrameworkCore;
using OmniAI.Application.AiAgents;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Tenants;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Security;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.IntegrationTests.Knowledge;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Phase 37.5a: the read models behind the knowledge, agent and public-chat
/// changes, against a real PostgreSQL -- what they return, that the workspace
/// bounds them, and what they cost in statements.
/// </summary>
public sealed class ScreensReadModelTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private readonly KnowledgeWorld _world = new(database);

    // ---- knowledge (rows 15, 16) -------------------------------------------

    [Fact]
    public async Task A_knowledge_page_costs_the_same_statements_whatever_its_length()
    {
        int small = await StatementsForKnowledgePageAsync(documents: 2);
        int large = await StatementsForKnowledgePageAsync(documents: 9);

        Assert.Equal(small, large);
    }

    [Fact]
    public async Task The_list_carries_the_latest_attempt_and_the_source_size()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await AttemptAsync(workspace, document.Value.Id, KnowledgeIndexAttemptFailures.ProviderTimeout, DateTimeOffset.UtcNow.AddMinutes(-5));
        await AttemptAsync(workspace, document.Value.Id, KnowledgeIndexAttemptFailures.QuotaExhausted, DateTimeOffset.UtcNow);
        await SourceAsync(workspace, document.Value.Id, byteLength: 4_321);

        await using OmniAiDbContext context = database.CreateContext();
        KnowledgeDocumentSummary item = Assert.Single(
            (await new KnowledgeDocumentRepository(context, KnowledgeWorld.Embeddings()).ListAsync(workspace, 1, 20)).Items);

        Assert.Equal(4_321, item.SourceByteLength);
        Assert.NotNull(item.LastIndexAttempt);
        Assert.Equal(KnowledgeIndexAttemptStatus.Failed, item.LastIndexAttempt.Status);
        Assert.Equal(KnowledgeIndexAttemptFailures.QuotaExhausted, item.LastIndexAttempt.FailureCode);
    }

    [Fact]
    public async Task The_latest_attempt_is_found_only_in_its_own_workspace()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        TenantId other = await _world.WorkspaceAsync("Other company");
        Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace);

        await AttemptAsync(workspace, document.Value.Id, KnowledgeIndexAttemptFailures.ProviderUnavailable, DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = database.CreateContext();
        KnowledgeIndexAttemptStore store = new(context);

        Assert.NotNull(await store.FindLatestAsync(workspace, document.Value.Id));
        Assert.Null(await store.FindLatestAsync(other, document.Value.Id));
    }

    // ---- agent channels (row 18) -------------------------------------------

    [Theory]
    [InlineData(AiAgentChannelKinds.None, 0)]
    [InlineData(AiAgentChannelKinds.WebChat, 1)]
    [InlineData(AiAgentChannelKinds.WhatsApp, 1)]
    [InlineData(AiAgentChannelKinds.WebChat | AiAgentChannelKinds.WhatsApp, 2)]
    public async Task Channels_cost_one_statement_per_visible_kind_for_a_whole_page(AiAgentChannelKinds kinds, int statements)
    {
        TenantId workspace = await _world.WorkspaceAsync();
        List<AiAgentId> agents = [];

        for (int index = 0; index < 5; index++)
        {
            AiAgent agent = await _world.AgentAsync(workspace, $"Agent {index}");
            await ChannelsAsync(workspace, agent.Id);
            agents.Add(agent.Id);
        }

        RecordedSql recorded = new();
        await using OmniAiDbContext context = database.CreateContext(recorded);

        IReadOnlyDictionary<AiAgentId, IReadOnlyList<AiAgentChannelView>> found =
            await new AiAgentChannelReader(context).ListAsync(workspace, agents, kinds);

        Assert.Equal(statements, recorded.Statements.Count);
        Assert.Equal(kinds == AiAgentChannelKinds.None ? 0 : agents.Count, found.Count);
    }

    [Fact]
    public async Task Another_workspaces_channels_are_never_read_for_an_agent_id()
    {
        TenantId workspace = await _world.WorkspaceAsync();
        TenantId other = await _world.WorkspaceAsync("Other company");
        AiAgent agent = await _world.AgentAsync(workspace);
        await ChannelsAsync(workspace, agent.Id);

        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyDictionary<AiAgentId, IReadOnlyList<AiAgentChannelView>> found = await new AiAgentChannelReader(context)
            .ListAsync(other, [agent.Id], AiAgentChannelKinds.WebChat | AiAgentChannelKinds.WhatsApp);

        Assert.Empty(found);
    }

    // ---- the workspace name (rows 20, 20a) ----------------------------------

    [Fact]
    public async Task A_rename_and_its_audit_record_commit_together()
    {
        TenantId workspace = await _world.WorkspaceAsync("Before");

        ConcurrencyToken version;
        await using (OmniAiDbContext context = database.CreateContext())
        {
            TenantRepository tenants = new(context);
            Versioned<Tenant> stored = (await tenants.FindVersionedAsync(workspace))!;
            stored.Value.Rename(TenantName.Create("After"));

            version = await tenants.RenameAsync(stored.Value, stored.Version, Audit(workspace));

            Assert.NotEqual(stored.Version, version);
        }

        Assert.Equal("After", await NameAsync(workspace));
        Assert.Equal(1, await AuditsAsync(workspace));
    }

    [Fact]
    public async Task A_stale_rename_writes_neither_the_name_nor_the_audit_record()
    {
        TenantId workspace = await _world.WorkspaceAsync("Before");

        ConcurrencyToken stale;
        await using (OmniAiDbContext first = database.CreateContext())
        {
            TenantRepository tenants = new(first);
            Versioned<Tenant> stored = (await tenants.FindVersionedAsync(workspace))!;
            stale = stored.Version;
            stored.Value.Rename(TenantName.Create("Winner"));
            await tenants.RenameAsync(stored.Value, stored.Version, Audit(workspace));
        }

        await using (OmniAiDbContext second = database.CreateContext())
        {
            TenantRepository tenants = new(second);
            Versioned<Tenant> stored = (await tenants.FindVersionedAsync(workspace))!;
            stored.Value.Rename(TenantName.Create("Loser"));

            await Assert.ThrowsAsync<TenantVersionMismatchException>(
                () => tenants.RenameAsync(stored.Value, stale, Audit(workspace)));
        }

        Assert.Equal("Winner", await NameAsync(workspace));
        Assert.Equal(1, await AuditsAsync(workspace));
    }

    [Fact]
    public async Task The_public_key_read_returns_the_workspace_name_and_the_appearance_in_one_statement()
    {
        TenantId workspace = await _world.WorkspaceAsync("Northwind Retail");
        AiAgent agent = await _world.AgentAsync(workspace);
        WebChatPublicKey key = await WebChatAsync(workspace, agent.Id);

        RecordedSql recorded = new();
        await using OmniAiDbContext context = database.CreateContext(recorded);

        PublicWebChatEndpoint? found = await new WebChatEndpointRepository(context).FindByPublicKeyAsync(key);

        Assert.NotNull(found);
        Assert.Equal("Northwind Retail", found.WorkspaceName);
        Assert.Equal(WebChatWidgetSettings.Default, found.Widget);
        Assert.Single(recorded.Statements);
    }

    // ---- helpers -------------------------------------------------------------

    private async Task<int> StatementsForKnowledgePageAsync(int documents)
    {
        TenantId workspace = await _world.WorkspaceAsync();

        for (int index = 0; index < documents; index++)
        {
            Versioned<KnowledgeDocument> document = await _world.DocumentAsync(workspace, $"Document {index}");
            await AttemptAsync(workspace, document.Value.Id, KnowledgeIndexAttemptFailures.ProviderUnavailable, DateTimeOffset.UtcNow);
            await SourceAsync(workspace, document.Value.Id, byteLength: 100 + index);
        }

        RecordedSql recorded = new();
        await using OmniAiDbContext context = database.CreateContext(recorded);

        KnowledgeDocumentPage page = await new KnowledgeDocumentRepository(context, KnowledgeWorld.Embeddings())
            .ListAsync(workspace, 1, 50);

        Assert.Equal(documents, page.Items.Count);
        Assert.All(page.Items, item => Assert.NotNull(item.LastIndexAttempt));
        Assert.All(page.Items, item => Assert.NotNull(item.SourceByteLength));

        return recorded.Statements.Count;
    }

    private async Task AttemptAsync(TenantId workspace, KnowledgeDocumentId documentId, string failureCode, DateTimeOffset startedAt)
    {
        KnowledgeIndexAttempt attempt = KnowledgeIndexAttempt.Start(workspace, documentId, startedAt);

        await using OmniAiDbContext context = database.CreateContext();
        KnowledgeIndexAttemptStore store = new(context);

        await store.StartAsync(attempt);
        await store.SettleAsync(attempt.Id, KnowledgeIndexAttemptStatus.Failed, failureCode, alert: false, startedAt.AddSeconds(2));
    }

    private async Task SourceAsync(TenantId workspace, KnowledgeDocumentId documentId, long byteLength)
    {
        await using OmniAiDbContext context = database.CreateContext();

        context.KnowledgeSources.Add(KnowledgeSource.Create(
            workspace,
            documentId,
            SourceFileName.Create("policy.txt"),
            KnowledgeFileType.Text,
            byteLength,
            ContentFingerprint.From(new byte[ContentFingerprint.Length]),
            DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();
    }

    private async Task ChannelsAsync(TenantId workspace, AiAgentId agentId)
    {
        await WebChatAsync(workspace, agentId);

        await using OmniAiDbContext context = database.CreateContext();

        context.WhatsAppConnections.Add(WhatsAppConnection.Create(
            workspace,
            agentId,
            WabaId.Create("111222333444555"),
            PhoneNumberId.Create(Random.Shared.NextInt64(100_000_000_000_000, 999_999_999_999_999)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)),
            displayPhoneNumber: null,
            DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();
    }

    private async Task<WebChatPublicKey> WebChatAsync(TenantId workspace, AiAgentId agentId)
    {
        WebChatPublicKey key = WebChatPublicKey.Create(
            Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_'));

        await using OmniAiDbContext context = database.CreateContext();
        context.WebChatEndpoints.Add(WebChatEndpoint.Create(workspace, agentId, key, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        return key;
    }

    private static SecurityEvent Audit(TenantId workspace) =>
        SecurityEvent.Record(SecurityEventKinds.WorkspaceRenamed, DateTimeOffset.UtcNow, tenantId: workspace);

    private async Task<string> NameAsync(TenantId workspace)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return (await context.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == workspace)).Name.Value;
    }

    private async Task<int> AuditsAsync(TenantId workspace)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM security_events WHERE kind = 'workspace.renamed' AND tenant_id = {workspace.Value}")
            .SingleAsync();
    }
}
