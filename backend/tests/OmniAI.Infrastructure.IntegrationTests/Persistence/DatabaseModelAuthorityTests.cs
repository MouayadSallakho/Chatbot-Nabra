using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The final schema is derivable from the EF model: the Phase 33.1 rule, held by
/// a test rather than by memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The central check compares two real databases.</strong> One is built
/// by applying the repository's migrations, as every other suite here is. The
/// other is built from the model alone: EF's differ from nothing to the current
/// model, rendered by Npgsql's migrations SQL generator -- what a freshly
/// scaffolded Initial migration would run. Both are described from the
/// PostgreSQL catalog (tables, columns, types, nullability, defaults, keys,
/// foreign keys and their delete rules, checks, index definitions and predicates,
/// extensions, sequences), and the descriptions must be identical.
/// </para>
/// <para>
/// That catches both directions of drift. Raw SQL or a hand-edited operation in a
/// migration makes the migrated database hold something the model does not; a
/// model change without a migration makes it lack something the model has. No
/// migration file is read, so the test does not care how a migration is written,
/// only what it produces.
/// </para>
/// <para>
/// The remaining tests pin the specific invariants Phase 33.1 moved into the
/// model, so a failure says which one broke rather than only that the two
/// databases differ.
/// </para>
/// </remarks>
public sealed class DatabaseModelAuthorityTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>, IAsyncLifetime
{
    private const string ConnectionEnvironmentVariable = "OMNIAI_TEST_POSTGRES";

    /// <summary>One line per semantic fact; names are part of the fact.</summary>
    private const string DescribeSchema = """
        SELECT 'EXT ' || extname FROM pg_extension WHERE extname <> 'plpgsql'
        UNION ALL
        SELECT 'SEQ ' || c.relname
          FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE c.relkind = 'S' AND n.nspname = 'public'
           AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype IN ('a', 'i'))
        UNION ALL
        SELECT 'COL ' || c.relname || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod)
               || CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE ' NULL' END
               || CASE WHEN a.attidentity <> '' THEN ' IDENTITY ' || a.attidentity::text ELSE '' END
               || COALESCE(' DEFAULT ' || pg_get_expr(ad.adbin, ad.adrelid), '')
          FROM pg_attribute a
          JOIN pg_class c ON c.oid = a.attrelid
          JOIN pg_namespace n ON n.oid = c.relnamespace
          LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
         WHERE c.relkind = 'r' AND n.nspname = 'public' AND a.attnum > 0 AND NOT a.attisdropped
           AND c.relname <> '__EFMigrationsHistory'
        UNION ALL
        SELECT 'CON ' || c.relname || ' ' || con.conname || ' ' || pg_get_constraintdef(con.oid)
          FROM pg_constraint con
          JOIN pg_class c ON c.oid = con.conrelid
          JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = 'public' AND c.relname <> '__EFMigrationsHistory'
        UNION ALL
        SELECT 'IDX ' || pg_get_indexdef(i.indexrelid)
          FROM pg_index i
          JOIN pg_class t ON t.oid = i.indrelid
          JOIN pg_namespace n ON n.oid = t.relnamespace
         WHERE n.nspname = 'public' AND t.relname <> '__EFMigrationsHistory'
           AND NOT EXISTS (SELECT 1 FROM pg_constraint con WHERE con.conindid = i.indexrelid AND con.conrelid = i.indrelid)
        ORDER BY 1
        """;

    private string _modelDatabaseName = string.Empty;

    private string _modelConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        string maintenance = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? throw new InvalidOperationException($"{ConnectionEnvironmentVariable} is not set.");

        _modelDatabaseName = $"omniai_test_model_{Guid.NewGuid():N}";
        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{_modelDatabaseName}\"");

        _modelConnectionString = new NpgsqlConnectionStringBuilder(maintenance)
        {
            Database = _modelDatabaseName,
        }.ConnectionString;

        // The schema from the model alone: no migration in the repository is read.
        using OmniAiDbContext context = database.CreateContext();

        IModel model = context.GetService<IDesignTimeModel>().Model;
        IReadOnlyList<MigrationOperation> operations = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());
        IReadOnlyList<MigrationCommand> commands = context
            .GetService<IMigrationsSqlGenerator>()
            .Generate(operations, model);

        await using NpgsqlConnection connection = new(_modelConnectionString);
        await connection.OpenAsync();

        foreach (MigrationCommand command in commands)
        {
            await using NpgsqlCommand sql = new(command.CommandText, connection);
            await sql.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_modelDatabaseName.Length == 0)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await PostgresTestDatabase.DropDatabaseAsync(
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)!,
            _modelDatabaseName);
    }

    [Fact]
    public async Task The_migrated_schema_is_exactly_the_schema_the_model_derives()
    {
        IReadOnlyList<string> migrated = await DescribeAsync(database.ConnectionString);
        IReadOnlyList<string> derived = await DescribeAsync(_modelConnectionString);

        // Reported as the two differences rather than two long lists, so a failure
        // names the object that drifted.
        Assert.Empty(migrated.Except(derived, StringComparer.Ordinal));
        Assert.Empty(derived.Except(migrated, StringComparer.Ordinal));
        Assert.True(migrated.Count > 500, $"Expected a full schema description; got {migrated.Count} facts.");
    }

    [Fact]
    public async Task The_model_declares_the_vector_extension_and_the_chunk_table()
    {
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM pg_extension WHERE extname = 'vector'"));

        Assert.Equal(
            "vector(1536) NOT NULL",
            await ScalarAsync(
                """
                SELECT format_type(a.atttypid, a.atttypmod) || CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE '' END
                  FROM pg_attribute a
                 WHERE a.attrelid = 'knowledge_chunks'::regclass AND a.attname = 'embedding'
                """));
    }

    [Fact]
    public async Task The_chunk_embedding_has_an_hnsw_cosine_index()
    {
        // Method and operator class read from the catalog, not parsed from DDL.
        Assert.Equal(
            "hnsw vector_cosine_ops",
            await ScalarAsync(
                """
                SELECT am.amname || ' ' || opc.opcname
                  FROM pg_index i
                  JOIN pg_class ic ON ic.oid = i.indexrelid
                  JOIN pg_am am ON am.oid = ic.relam
                  JOIN pg_opclass opc ON opc.oid = i.indclass[0]
                 WHERE ic.relname = 'ix_knowledge_chunks_embedding'
                """));
    }

    [Fact]
    public async Task Every_conversation_must_have_a_customer()
    {
        Assert.Equal(
            "NO",
            await ScalarAsync(
                """
                SELECT is_nullable FROM information_schema.columns
                 WHERE table_schema = 'public' AND table_name = 'conversations' AND column_name = 'customer_id'
                """));
    }

    [Theory]
    [InlineData("fk_knowledge_chunks_document", "FOREIGN KEY (knowledge_document_id, tenant_id) REFERENCES knowledge_documents(id, tenant_id) ON DELETE CASCADE")]
    [InlineData("fk_knowledge_sources_document", "FOREIGN KEY (knowledge_document_id, tenant_id) REFERENCES knowledge_documents(id, tenant_id) ON DELETE CASCADE")]
    [InlineData("fk_conversations_assigned_member", "FOREIGN KEY (assigned_to, tenant_id) REFERENCES tenant_memberships(user_id, tenant_id) ON DELETE RESTRICT")]
    [InlineData("fk_human_replies_sender_member", "FOREIGN KEY (sender_user_id, tenant_id) REFERENCES tenant_memberships(user_id, tenant_id) ON DELETE RESTRICT")]
    [InlineData("fk_conversations_customer", "FOREIGN KEY (tenant_id, customer_id) REFERENCES customers(tenant_id, id) ON DELETE RESTRICT")]
    [InlineData("fk_tenant_memberships_user", "FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE RESTRICT")]
    [InlineData("fk_notifications_recipient_user", "FOREIGN KEY (recipient_user_id) REFERENCES users(id) ON DELETE CASCADE")]
    [InlineData("fk_notification_emails_recipient_user", "FOREIGN KEY (recipient_user_id) REFERENCES users(id) ON DELETE CASCADE")]
    [InlineData("fk_notification_preferences_user", "FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE")]
    public async Task Foreign_keys_that_used_to_be_hand_written_come_from_the_model(string name, string definition)
    {
        Assert.Equal(definition, await ScalarAsync($"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = '{name}'"));
    }

    [Theory]
    [InlineData("ck_knowledge_chunks_ordinal")]
    [InlineData("ck_knowledge_documents_index_complete")]
    [InlineData("ck_web_chat_sessions_embedded_origin")]
    public async Task Check_constraints_that_used_to_be_hand_written_come_from_the_model(string name)
    {
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM pg_constraint WHERE contype = 'c' AND conname = '{name}'"));
    }

    [Theory]
    [InlineData("ix_conversations_assigned_to", "WHERE (assigned_to IS NOT NULL)")]
    [InlineData("ux_whatsapp_contacts_current", "WHERE is_current")]
    public async Task Partial_indexes_that_used_to_be_hand_written_come_from_the_model(string name, string predicate)
    {
        string? definition = await ScalarAsync($"SELECT indexdef FROM pg_indexes WHERE indexname = '{name}'") as string;

        Assert.NotNull(definition);
        Assert.EndsWith(predicate, definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_table_has_an_application_owned_xmin_column()
    {
        Assert.Equal(
            0L,
            await ScalarAsync(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'xmin'"));
    }

    [Fact]
    public async Task The_shipped_plan_catalogue_is_seeded_by_the_model()
    {
        Assert.Equal(
            "development,free,legacy,starter",
            await ScalarAsync("SELECT string_agg(code, ',' ORDER BY code) FROM plans"));
    }

    private async Task<object?> ScalarAsync(string sql) => await database.ExecuteScalarAsync(sql);

    private static async Task<IReadOnlyList<string>> DescribeAsync(string connectionString)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(DescribeSchema, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        List<string> facts = [];

        while (await reader.ReadAsync())
        {
            facts.Add(reader.GetString(0));
        }

        return facts;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
