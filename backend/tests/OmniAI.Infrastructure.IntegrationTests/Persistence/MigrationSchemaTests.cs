using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Guards on the migration itself, rather than on the behaviour it enables.
/// </summary>
/// <remarks>
/// The migration is generated code that has to be edited by hand, so it can
/// regress silently the next time it is regenerated. These tests make that
/// regression a failing test instead of a broken deployment.
/// </remarks>
public sealed class MigrationSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public void The_migration_script_never_creates_an_xmin_column()
    {
        // Npgsql 10 scaffolds `xmin` as a physical column for a row-version
        // concurrency token. PostgreSQL rejects that: xmin is a system column
        // that already exists on every table. The generated line is therefore
        // removed by hand, and this is what notices if it comes back.
        using OmniAiDbContext context = database.CreateContext();

        string script = context.GetService<IMigrator>().GenerateScript();

        Assert.DoesNotContain("\"xmin\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ADD COLUMN xmin", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xmin xid", script, StringComparison.OrdinalIgnoreCase);

        // The token itself must still be in the model, or the concurrency tests
        // would be passing for the wrong reason. Checked for every aggregate that
        // is mutable, so an entity added later without a token is noticed here
        // rather than by a lost update in production.
        Type[] concurrencyTracked =
        [
            typeof(Domain.Tenants.Tenant),
            typeof(Domain.Conversations.Conversation),
            typeof(Domain.AiAgents.AiAgent),
        ];

        foreach (Type aggregate in concurrencyTracked)
        {
            Assert.NotNull(
                context.Model.FindEntityType(aggregate)!.FindProperty(PersistenceSchemaNames.Xmin));
        }
    }

    [Fact]
    public void The_ai_agent_concurrency_token_is_read_only_and_never_written()
    {
        // xmin is PostgreSQL's own column. EF must read it and put it in the
        // UPDATE predicate, and must never try to set it -- attempting that would
        // make every write fail.
        using OmniAiDbContext context = database.CreateContext();

        Microsoft.EntityFrameworkCore.Metadata.IProperty token =
            context.Model.FindEntityType(typeof(Domain.AiAgents.AiAgent))!
                .FindProperty(PersistenceSchemaNames.Xmin)!;

        Assert.True(token.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, token.ValueGenerated);
    }

    [Fact]
    public async Task Every_migration_is_applied_to_the_test_database()
    {
        // Each accepted migration is a committed baseline that was not rewritten;
        // every phase that changes the schema adds one beside them. All must be
        // present, in order, or these suites are testing a schema nobody will
        // deploy.
        using OmniAiDbContext context = database.CreateContext();

        string[] applied = [.. await context.Database.GetAppliedMigrationsAsync()];

        Assert.Equal(15, applied.Length);
        Assert.EndsWith("InitialPersistence", applied[0], StringComparison.Ordinal);
        Assert.EndsWith("AddAiAgents", applied[1], StringComparison.Ordinal);
        Assert.EndsWith("AddIdentityAndTenantMemberships", applied[2], StringComparison.Ordinal);
        Assert.EndsWith("AddTenantRolesAndPermissions", applied[3], StringComparison.Ordinal);
        Assert.EndsWith(
            "AddTenantInvitationsAndMemberAccessVersioning",
            applied[4],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddWebsiteChatAndConversationRuntime",
            applied[5],
            StringComparison.Ordinal);
        Assert.EndsWith("AddWebChatSessionExpiry", applied[6], StringComparison.Ordinal);
        Assert.EndsWith(
            "AddKnowledgeBaseAndVectorIndex",
            applied[7],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddWebChatWidgetEmbedding",
            applied[8],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddHumanHandoffAndAssignment",
            applied[9],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddWhatsAppCloudApiChannel",
            applied[10],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddKnowledgeFileSources",
            applied[11],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddAnalyticsIndexes",
            applied[12],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddAiProviderUsageAccounting",
            applied[13],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "AddWorkspacePlansAndEntitlements",
            applied[14],
            StringComparison.Ordinal);
    }

    /// <remarks>
    /// Phase 22 added no table and no column. What it added is two indexes, and
    /// they are asserted here because an index that quietly failed to be created
    /// would turn a bounded dashboard into a full scan with nothing to show for
    /// it -- a performance regression no functional test would notice.
    /// </remarks>
    [Fact]
    public async Task The_analytics_indexes_exist_on_the_columns_analytics_filters_by()
    {
        object? conversations = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE tablename = 'conversations' "
            + "AND indexname = 'ix_conversations_tenant_started'");

        Assert.NotNull(conversations);
        Assert.Contains("tenant_id", (string)conversations, StringComparison.Ordinal);
        Assert.Contains("started_at", (string)conversations, StringComparison.Ordinal);

        object? deliveries = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE tablename = 'whatsapp_deliveries' "
            + "AND indexname = 'ix_whatsapp_deliveries_tenant_created'");

        Assert.NotNull(deliveries);
        Assert.Contains("tenant_id", (string)deliveries, StringComparison.Ordinal);
        Assert.Contains("created_at", (string)deliveries, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The other half of the Phase 22 index decision: nothing was added to the
    /// tables that joins already reach by an indexed key. If a later phase adds
    /// one it should be because a measurement asked for it, and this test failing
    /// is the prompt to write that measurement down.
    /// </remarks>
    [Fact]
    public async Task Phase_22_added_no_index_to_messages_or_conversation_turns()
    {
        object? count = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_indexes "
            + "WHERE tablename IN ('messages', 'conversation_turns')");

        // Two apiece: the primary key and the one join key each already had.
        Assert.Equal(5L, Convert.ToInt64(count, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_knowledge_sources_table_has_no_physical_xmin_column()
    {
        // The same hand-edit guard, aimed at the table Phase 21 added. Npgsql's
        // scaffolder emitted the column again and it was removed again by hand;
        // this is what notices if a future regeneration puts it back.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'knowledge_sources' "
            + "AND column_name = 'xmin'");

        Assert.Equal(0L, columns as long?);
    }

    [Fact]
    public async Task A_knowledge_source_can_only_point_at_a_document_in_its_own_workspace()
    {
        // The composite foreign key, asserted to exist rather than trusted from
        // the migration. Without it a source row could name another workspace's
        // document, and only application code would stand between that and one
        // company's file metadata attached to another company's knowledge.
        object? definition = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'fk_knowledge_sources_document'");

        string constraint = definition as string ?? string.Empty;

        Assert.Contains("knowledge_document_id, tenant_id", constraint, StringComparison.Ordinal);
        Assert.Contains("knowledge_documents(id, tenant_id)", constraint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_whatsapp_connections_table_has_no_physical_xmin_column()
    {
        // The same hand-edit guard, aimed at the table Phase 20 added. Npgsql's
        // scaffolder emitted the column again and it was removed again by hand;
        // this is what notices if a future regeneration puts it back and turns a
        // working entity tag into a column that shadows PostgreSQL's own.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'whatsapp_connections' "
            + "AND column_name = 'xmin'");

        Assert.Equal(0L, columns as long?);
    }

    [Fact]
    public async Task The_ai_agents_table_has_no_user_column_named_xmin()
    {
        // The hand-edit guard, aimed at the table Phase 7 added. The scaffolder
        // emitted the column again for ai_agents and it was removed again by hand,
        // so this is what notices if a future regeneration puts it back.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'ai_agents' AND column_name = 'xmin'");

        Assert.Equal(0L, columns as long?);
    }

    [Fact]
    public async Task The_applied_schema_has_no_user_column_named_xmin()
    {
        // Scoped to the public schema on purpose: information_schema.columns also
        // reports pg_catalog's own relations, some of which really do have a
        // column named xmin, and those are not this migration's doing.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND column_name = 'xmin'");

        Assert.Equal(0L, columns as long?);
    }

    [Fact]
    public async Task The_migration_creates_the_message_ingestion_sequence()
    {
        object? sequences = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_sequences WHERE sequencename = 'message_ingestion_sequence'");

        Assert.Equal(1L, sequences as long?);
    }

    [Fact]
    public async Task The_ingestion_sequence_column_is_a_plain_bigint_not_an_identity()
    {
        // Deliberately not an identity column: EF Core does not order its inserts
        // within a batch, so letting the database fill this in produced values
        // that did not follow append order.
        object? description = await database.ExecuteScalarAsync(
            "SELECT data_type || '/' || is_identity FROM information_schema.columns " +
            "WHERE table_name = 'messages' AND column_name = 'ingestion_sequence'");

        Assert.Equal("bigint/NO", description as string);
    }

    /// <summary>
    /// Mirrors the Infrastructure-internal schema names this test project cannot
    /// see. Kept deliberately tiny; if it drifts, the assertions above fail.
    /// </summary>
    private static class PersistenceSchemaNames
    {
        public const string Xmin = "xmin";
    }
}
