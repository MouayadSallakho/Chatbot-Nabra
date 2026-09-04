using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        // would be passing for the wrong reason.
        Assert.NotNull(
            context.Model.FindEntityType(typeof(Domain.Conversations.Conversation))!
                .FindProperty(PersistenceSchemaNames.Xmin));
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
