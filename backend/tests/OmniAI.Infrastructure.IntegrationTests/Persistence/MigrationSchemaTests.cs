using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Guards on the migration itself, rather than on the behaviour it enables.
/// </summary>
/// <remarks>
/// Migrations are generated from the model and are never edited by hand. Since
/// Phase 33.1 the repository holds one Initial baseline generated that way; the
/// hand-edited development history it replaced remains in Git. The schema a
/// migration produces can still regress silently when the model changes, so these
/// tests make that regression a failing test instead of a broken deployment — and
/// the xmin tests check the model itself, not a migration file.
/// <c>DatabaseModelAuthorityTests</c> holds the wider rule that the migrated
/// schema is exactly the schema the model derives.
/// </remarks>
public sealed class MigrationSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public void The_migration_script_never_creates_an_xmin_column()
    {
        // The migration C# does name `xmin` for every row-version token -- EF's
        // differ is provider-agnostic -- but Npgsql's SQL generator recognises
        // PostgreSQL's system column and emits nothing for it. This checks the
        // rendered script, which is what reaches the database.
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
    public async Task The_initial_baseline_and_every_later_migration_are_applied_in_order()
    {
        // Phase 33.1 replaced the development migration history with one Initial
        // generated from the model and never edited (the old history remains in
        // Git). Every phase that changes the schema adds one generated migration
        // beside it; all must be present, in order, or these suites are testing a
        // schema nobody will deploy.
        using OmniAiDbContext context = database.CreateContext();

        string[] applied = [.. await context.Database.GetAppliedMigrationsAsync()];
        string[] known = [.. context.Database.GetMigrations()];

        // Every migration this build knows, applied, in order -- and the history
        // still starts at the one Initial baseline (no rebaseline since 33.1).
        Assert.Equal(known, applied);
        Assert.EndsWith("_Initial", applied[0], StringComparison.Ordinal);
        Assert.Single(applied, migration => migration.EndsWith("_Initial", StringComparison.Ordinal));

        // Phase 35 added exactly one; Phase 37.3 added the four the approved plan
        // lists for it (M1, M3, M2, M2a), in that order; Phase 37.4 the three it
        // lists (M6, M4, M5), in the plan's order; Phase 37.6 the two it lists
        // (M7, M8); Phase 37.7 the two it lists (M9, M10); Phase 39.1 the two its
        // approved plan lists (AddUserDisplayName, AddWebChatGreetingPerLanguage).
        Assert.Equal(25, applied.Length);
        Assert.EndsWith("_ProductionReadinessHardening", applied[1], StringComparison.Ordinal);
        Assert.EndsWith("_AddUserLanguage", applied[2], StringComparison.Ordinal);
        Assert.EndsWith("_AddSecurityEvents", applied[3], StringComparison.Ordinal);
        Assert.EndsWith("_AddAccountEmailOutbox", applied[4], StringComparison.Ordinal);
        Assert.EndsWith("_AddInvitationEmail", applied[5], StringComparison.Ordinal);
        Assert.EndsWith("_AddConversationHandlingIndex", applied[6], StringComparison.Ordinal);
        Assert.EndsWith("_AddConversationHandlingEvents", applied[7], StringComparison.Ordinal);
        Assert.EndsWith("_AddConversationReads", applied[8], StringComparison.Ordinal);
        Assert.EndsWith("_AddWorkspaceTimeZone", applied[9], StringComparison.Ordinal);
        Assert.EndsWith("_AddReportExecutionTimeZone", applied[10], StringComparison.Ordinal);
        Assert.EndsWith("_AddReportExecutionLanguage", applied[11], StringComparison.Ordinal);
        Assert.EndsWith("_AddReportScheduleLanguage", applied[12], StringComparison.Ordinal);
        Assert.EndsWith("_AddUserDisplayName", applied[13], StringComparison.Ordinal);
        Assert.EndsWith("_AddWebChatGreetingPerLanguage", applied[14], StringComparison.Ordinal);
        Assert.EndsWith("_AddInboxSearchIndexes", applied[15], StringComparison.Ordinal);
        Assert.EndsWith("_AddWorkspaceQuickReplies", applied[16], StringComparison.Ordinal);
        Assert.EndsWith("_AddMessengerSimulator", applied[17], StringComparison.Ordinal);
        Assert.EndsWith("_AddMessengerDeliveryStatus", applied[18], StringComparison.Ordinal);
        Assert.EndsWith("_AddInstagramSimulator", applied[19], StringComparison.Ordinal);
        Assert.EndsWith("_InstagramDeliveryOwnership", applied[20], StringComparison.Ordinal);
        Assert.EndsWith("_InstagramHandoverOrdering", applied[21], StringComparison.Ordinal);
        Assert.EndsWith("_MessageAttachments", applied[22], StringComparison.Ordinal);
        Assert.EndsWith("_InboxRouting", applied[23], StringComparison.Ordinal);
        Assert.EndsWith("_InboxBusinessHours", applied[24], StringComparison.Ordinal);
    }

    /// <remarks>
    /// Phase 37.7 (M9, M10): each report execution and each schedule carries the
    /// language of its file, exactly as the approved plan describes the columns:
    /// <c>varchar(8) NOT NULL DEFAULT 'en'</c>, so every row made before reads
    /// English, which is what it was.
    /// </remarks>
    [Theory]
    [InlineData("report_executions")]
    [InlineData("report_schedules")]
    public async Task Report_language_is_a_required_short_code_defaulting_to_english(string table)
    {
        object? definition = await database.ExecuteScalarAsync(
            "SELECT data_type || '/' || character_maximum_length || '/' || is_nullable || '/' || column_default "
            + "FROM information_schema.columns "
            + $"WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = 'language'");

        Assert.Equal("character varying/8/NO/'en'::character varying", definition as string);
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
    public async Task Message_and_turn_indexes_cover_ingestion_reconciliation_and_inbox_search()
    {
        object? count = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_indexes "
            + "WHERE tablename IN ('messages', 'conversation_turns')");

        // Five original indexes, the stale-turn reconciler's partial index,
        // and the inbox's substring index for historical message text.
        Assert.Equal(7L, Convert.ToInt64(count, CultureInfo.InvariantCulture));

        object? textIndex = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'messages' AND indexname = 'ix_messages_text_trgm'");
        Assert.Contains("USING gin (text gin_trgm_ops)", textIndex as string, StringComparison.Ordinal);

        object? generating = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_conversation_turns_generating'");

        Assert.Contains("WHERE ((status)::text = 'Generating'::text)", generating as string, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_knowledge_sources_table_has_no_physical_xmin_column()
    {
        // The xmin guard, aimed at the table Phase 21 added: its row-version
        // token must stay PostgreSQL's system column, never a physical one.
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
        // The xmin guard, aimed at the table Phase 20 added. A physical column
        // would turn a working entity tag into one that shadows PostgreSQL's own.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'whatsapp_connections' "
            + "AND column_name = 'xmin'");

        Assert.Equal(0L, columns as long?);
    }

    [Fact]
    public async Task The_ai_agents_table_has_no_user_column_named_xmin()
    {
        // The xmin guard, aimed at the table Phase 7 added.
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

    /// <remarks>
    /// <para>
    /// <strong>Tests the model, not a migration file.</strong> The full schema a
    /// fresh Initial migration would create is derived here from the current EF
    /// model, through EF's own model differ and Npgsql's own migrations SQL
    /// generator — the pipeline <c>dotnet ef migrations add</c> followed by
    /// <c>database update</c> runs. No migration in the repository is read.
    /// </para>
    /// <para>
    /// The differ is provider-agnostic, so it does emit a column operation for
    /// every <c>xmin</c> token; that is asserted too, because it proves the tokens
    /// are really mapped. What must never appear is <c>xmin</c> DDL: Npgsql knows
    /// it is a PostgreSQL system column and generates nothing for it. If a future
    /// mapping ever named it differently, typed it differently or took it off the
    /// system-column path, this fails.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_schema_derived_from_the_model_alone_creates_no_xmin_column()
    {
        using OmniAiDbContext context = database.CreateContext();

        IModel model = context.GetService<IDesignTimeModel>().Model;

        IReadOnlyList<MigrationOperation> operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

        int tokenEntities = model.GetEntityTypes()
            .Count(entity => entity.FindProperty(PersistenceSchemaNames.Xmin) is not null);

        int xminColumnOperations = operations.OfType<CreateTableOperation>()
            .SelectMany(table => table.Columns)
            .Count(column => column.Name == PersistenceSchemaNames.Xmin);

        Assert.True(tokenEntities >= 13, $"Expected every concurrency-guarded aggregate to map xmin; found {tokenEntities}.");
        Assert.Equal(tokenEntities, xminColumnOperations);

        string ddl = string.Join(
            "\n",
            context.GetService<IMigrationsSqlGenerator>()
                .Generate(operations, model)
                .Select(command => command.CommandText));

        Assert.Contains("CREATE TABLE ai_provider_usages", ddl, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE knowledge_index_attempts", ddl, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE data_protection_keys", ddl, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bxmin\b", ddl);
    }

    [Fact]
    public async Task Every_xmin_token_is_the_postgres_system_column_and_no_table_owns_one()
    {
        using OmniAiDbContext context = database.CreateContext();

        IProperty[] tokens =
        [
            .. context.Model.GetEntityTypes()
                .Select(entity => entity.FindProperty(PersistenceSchemaNames.Xmin))
                .OfType<IProperty>(),
        ];

        Assert.NotEmpty(tokens);
        Assert.All(tokens, token =>
        {
            Assert.True(token.IsConcurrencyToken, $"{token.DeclaringType.DisplayName()}.xmin is not a concurrency token.");
            Assert.Equal(ValueGenerated.OnAddOrUpdate, token.ValueGenerated);
            Assert.Equal(typeof(uint), token.ClrType);
            Assert.Equal("xid", token.GetColumnType());
            Assert.Equal(PersistenceSchemaNames.Xmin, token.GetColumnName());
        });

        // No application table in the migrated database has a column of its own
        // by that name, and on a guarded table the name resolves to the system
        // attribute (negative attnum), which PostgreSQL maintains itself.
        object? physical = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND column_name = 'xmin'");

        Assert.Equal(0L, physical as long?);

        object? systemAttribute = await database.ExecuteScalarAsync(
            "SELECT attnum FROM pg_attribute "
            + "WHERE attrelid = 'public.ai_provider_usages'::regclass AND attname = 'xmin'");

        Assert.True(Convert.ToInt32(systemAttribute, CultureInfo.InvariantCulture) < 0);
    }

    [Fact]
    public async Task The_phase_33_reliability_schema_exists_with_its_guards()
    {
        object? started = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_ai_provider_usages_open'");
        Assert.Contains("WHERE", started as string ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("'Started'", started as string ?? string.Empty, StringComparison.Ordinal);

        object? attempts = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_knowledge_index_attempts_started'");
        Assert.Contains("'Started'", attempts as string ?? string.Empty, StringComparison.Ordinal);

        object? foreignKey = await database.ExecuteScalarAsync(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint "
            + "WHERE conname = 'fk_knowledge_index_attempts_document'");
        Assert.Contains("knowledge_documents(id, tenant_id)", foreignKey as string ?? string.Empty, StringComparison.Ordinal);

        object? settled = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_constraint WHERE conname IN "
            + "('ck_knowledge_index_attempts_status', 'ck_knowledge_index_attempts_settled')");
        Assert.Equal(2L, settled as long?);

        object? keys = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.tables "
            + "WHERE table_schema = 'public' AND table_name = 'data_protection_keys'");
        Assert.Equal(1L, keys as long?);
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
