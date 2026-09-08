using System.Globalization;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Knowledge;

/// <summary>
/// The schema the knowledge base actually got, in a real PostgreSQL 17.
/// </summary>
/// <remarks>
/// Every assertion here reads the live catalogue rather than the EF model. The
/// model is what this platform believes; the catalogue is what a deployment will
/// have, and the two are only the same until somebody hand-edits a migration --
/// which this phase did, deliberately, twice.
/// </remarks>
public sealed class KnowledgeSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private readonly PostgresTestDatabase _database = database;

    [Fact]
    public async Task The_vector_extension_is_installed()
    {
        // Without it the chunk table cannot exist at all: its embedding column
        // names a type the extension provides.
        object? installed = await _database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_extension WHERE extname = 'vector'");

        Assert.Equal(1L, installed);
    }

    [Fact]
    public async Task The_embedding_column_is_a_vector_of_the_configured_width()
    {
        // Not JSON, not text, not an array. The column type is what makes the
        // distance operators and the index work; a text column would hold the same
        // digits and be useless for search.
        object? type = await _database.ExecuteScalarAsync(
            """
            SELECT format_type(a.atttypid, a.atttypmod)
              FROM pg_attribute a
              JOIN pg_class c ON c.oid = a.attrelid
             WHERE c.relname = 'knowledge_chunks'
               AND a.attname = 'embedding'
            """);

        Assert.Equal("vector(1536)", type as string);
    }

    [Fact]
    public async Task A_vector_of_the_wrong_width_is_refused_by_the_column()
    {
        // The last line of defence, below every check this platform makes itself.
        Npgsql.PostgresException failure = await Assert.ThrowsAsync<Npgsql.PostgresException>(
            () => _database.ExecuteScalarAsync("SELECT '[1,2,3]'::vector(1536)"));

        Assert.Contains("dimension", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_embedding_has_an_hnsw_index_for_cosine_distance()
    {
        // The operator class matters as much as the index: an index built for L2
        // distance would simply not be used by a query ordering on cosine, and the
        // only symptom would be a search that got slower as a workspace grew.
        object? definition = await _database.ExecuteScalarAsync(
            """
            SELECT indexdef FROM pg_indexes
             WHERE tablename = 'knowledge_chunks'
               AND indexname = 'ix_knowledge_chunks_embedding'
            """);

        string sql = Assert.IsType<string>(definition);

        Assert.Contains("USING hnsw", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("vector_cosine_ops", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("knowledge_documents")]
    [InlineData("knowledge_chunks")]
    [InlineData("ai_agent_knowledge_documents")]
    public async Task No_knowledge_table_has_a_user_column_named_xmin(string table)
    {
        // The hand-edit guard. The scaffolder emitted the column for
        // knowledge_documents and it was removed by hand; a physical column of
        // that name would shadow PostgreSQL's own and silently disable every
        // precondition on the table, with no error anywhere to say so.
        object? columns = await _database.ExecuteScalarAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = 'xmin'"));

        Assert.Equal(0L, columns);
    }

    [Fact]
    public async Task A_chunk_must_belong_to_the_same_workspace_as_its_document()
    {
        // The composite foreign key, not a handler. A chunk filed under a
        // workspace its document does not belong to cannot be written at all.
        object? constraint = await _database.ExecuteScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
             WHERE conname = 'fk_knowledge_chunks_document'
            """);

        string definition = Assert.IsType<string>(constraint);

        Assert.Contains("(knowledge_document_id, tenant_id)", definition, StringComparison.Ordinal);
        Assert.Contains("(id, tenant_id)", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assignment_must_name_an_agent_and_a_document_in_one_workspace()
    {
        // Two composite foreign keys over one tenant_id. A cross-workspace pair
        // would have to satisfy both at once with a single value, and cannot.
        object? agent = await _database.ExecuteScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
             WHERE conname = 'fk_ai_agent_knowledge_documents_agent'
            """);

        object? document = await _database.ExecuteScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
             WHERE conname = 'fk_ai_agent_knowledge_documents_document'
            """);

        Assert.Contains(
            "(ai_agent_id, tenant_id)",
            Assert.IsType<string>(agent),
            StringComparison.Ordinal);

        Assert.Contains(
            "(knowledge_document_id, tenant_id)",
            Assert.IsType<string>(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_document_cannot_be_assigned_to_one_agent_twice()
    {
        object? key = await _database.ExecuteScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
             WHERE conname = 'pk_ai_agent_knowledge_documents'
            """);

        Assert.Contains(
            "PRIMARY KEY (ai_agent_id, knowledge_document_id)",
            Assert.IsType<string>(key),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_cannot_hold_two_chunks_at_the_same_ordinal()
    {
        object? key = await _database.ExecuteScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
             WHERE conname = 'ux_knowledge_chunks_document_ordinal'
            """);

        Assert.Contains(
            "UNIQUE (knowledge_document_id, ordinal)",
            Assert.IsType<string>(key),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_metadata_is_all_three_columns_or_none()
    {
        // A row holding a fingerprint but no profile would be a document claiming
        // to be indexed by nothing in particular, and the readiness derivation
        // would have to guess what that meant.
        object? check = await _database.ExecuteScalarAsync(
            """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
             WHERE conname = 'ck_knowledge_documents_index_complete'
            """);

        string definition = Assert.IsType<string>(check);

        Assert.Contains("indexed_fingerprint IS NULL", definition, StringComparison.Ordinal);
        Assert.Contains("indexed_profile IS NOT NULL", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chunk_cannot_have_a_negative_ordinal()
    {
        object? check = await _database.ExecuteScalarAsync(
            """
            SELECT count(*) FROM pg_constraint
             WHERE conname = 'ck_knowledge_chunks_ordinal'
            """);

        Assert.Equal(1L, check);
    }

    [Fact]
    public async Task Deleting_a_document_takes_its_chunks_and_assignments_with_it()
    {
        // Cascade on both, so a deleted document cannot leave orphaned vectors
        // that nothing owns and nothing can reach.
        object? chunks = await _database.ExecuteScalarAsync(
            """
            SELECT confdeltype FROM pg_constraint
             WHERE conname = 'fk_knowledge_chunks_document'
            """);

        object? assignments = await _database.ExecuteScalarAsync(
            """
            SELECT confdeltype FROM pg_constraint
             WHERE conname = 'fk_ai_agent_knowledge_documents_document'
            """);

        // 'c' is CASCADE in the catalogue.
        Assert.Equal('c', chunks);
        Assert.Equal('c', assignments);
    }
}
