using Npgsql;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// What the Phase 19 migration actually built.
/// </summary>
/// <remarks>
/// The migration was edited by hand, so these assert the edits rather than the
/// scaffolder's output: the backfilled column, the composite foreign keys that
/// make a cross-workspace assignment unrepresentable, and the unique index the
/// reply idempotency depends on.
/// </remarks>
public sealed class HandoffSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database = database;

    [Fact]
    public async Task Every_conversation_has_a_handling_value()
    {
        // The scaffolder's NOT NULL with defaultValue: "" would have put an
        // empty string -- a value the enum has no member for -- into every
        // existing row, and the first read of an old conversation would have
        // failed rather than the deployment.
        string? nullable = await ScalarAsync(
            """
            SELECT is_nullable FROM information_schema.columns
            WHERE table_name = 'conversations' AND column_name = 'handling'
            """);

        Assert.Equal("NO", nullable);

        string? defaulted = await ScalarAsync(
            """
            SELECT column_default FROM information_schema.columns
            WHERE table_name = 'conversations' AND column_name = 'handling'
            """);

        // No default at all: the value is written by the application, and the
        // backfill was a one-off for rows that predate the column.
        Assert.Null(defaulted);
    }

    [Fact]
    public async Task An_assignment_cannot_name_somebody_from_another_workspace()
    {
        // The composite foreign key. Without it the only thing between a foreign
        // assignment and a customer's transcript in an outsider's inbox is an
        // application check.
        string? definition = await ScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
            WHERE conname = 'fk_conversations_assigned_member'
            """);

        Assert.NotNull(definition);
        Assert.Contains("assigned_to", definition, StringComparison.Ordinal);
        Assert.Contains("tenant_id", definition, StringComparison.Ordinal);
        Assert.Contains("tenant_memberships", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reply_cannot_be_attributed_to_somebody_from_another_workspace()
    {
        string? definition = await ScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
            WHERE conname = 'fk_human_replies_sender_member'
            """);

        Assert.NotNull(definition);
        Assert.Contains("sender_user_id", definition, StringComparison.Ordinal);
        Assert.Contains("tenant_id", definition, StringComparison.Ordinal);
        Assert.Contains("tenant_memberships", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_reply_key_cannot_be_stored_twice()
    {
        // The idempotency boundary. Two concurrent requests carrying the same
        // client message id both pass any in-process check; this index is the
        // only thing that can settle the race.
        string? definition = await ScalarAsync(
            $"""
            SELECT indexdef FROM pg_indexes
            WHERE tablename = '{PersistenceSchema.HumanRepliesTable}'
              AND indexname = 'ux_human_replies_client_message'
            """);

        Assert.NotNull(definition);
        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("conversation_id", definition, StringComparison.Ordinal);
        Assert.Contains("client_message_id", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_message_has_at_most_one_author()
    {
        string? definition = await ScalarAsync(
            $"""
            SELECT indexdef FROM pg_indexes
            WHERE tablename = '{PersistenceSchema.HumanRepliesTable}'
              AND indexname = 'ux_human_replies_message'
            """);

        Assert.NotNull(definition);
        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_scaffolders_redundant_tenant_index_was_not_kept()
    {
        // Nothing reads human replies by workspace alone. An index that serves
        // no query is write cost and disk for nothing.
        string? redundant = await ScalarAsync(
            $"""
            SELECT indexname FROM pg_indexes
            WHERE tablename = '{PersistenceSchema.HumanRepliesTable}'
              AND indexname = 'IX_conversation_human_replies_tenant_id'
            """);

        Assert.Null(redundant);
    }

    [Fact]
    public async Task No_physical_xmin_column_was_introduced()
    {
        // The platform's standing rule: xmin is PostgreSQL's own system column
        // and must never be shadowed by a real one.
        string? offending = await ScalarAsync(
            """
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name = 'xmin'
            LIMIT 1
            """);

        Assert.Null(offending);
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = new(_database.ConnectionString);

        await connection.OpenAsync();

        await using NpgsqlCommand command = new(sql, connection);

        object? value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? null : value.ToString();
    }
}
