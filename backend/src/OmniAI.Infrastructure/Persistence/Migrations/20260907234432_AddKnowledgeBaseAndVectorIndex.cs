using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the knowledge base: documents, their vector index, and which agents
    /// may draw on them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The <c>vector</c> extension is created first, and it has to be.</strong>
    /// The chunk table declares a column of type <c>vector(1536)</c>, which does
    /// not exist until the extension does. <c>IF NOT EXISTS</c> so the migration is
    /// safe on a database that already has it -- which is every database this has
    /// ever run against twice.
    /// </para>
    /// <para>
    /// <strong><c>knowledge_chunks</c> is written by hand, not scaffolded.</strong>
    /// It is not an EF entity: keeping the embedding out of the model is what makes
    /// it impossible for a query or a projection anywhere in the solution to load
    /// or publish a vector by accident. EF therefore knows nothing about this
    /// table, which is exactly the intent, and the table has to be described here
    /// in the SQL that creates it.
    /// </para>
    /// <para>
    /// <strong>The vector is a real <c>vector</c> column.</strong> Not JSON, not
    /// base64, not an array of text. That is what makes the distance operators and
    /// the HNSW index work at all; a text column would store the same digits and be
    /// useless for search.
    /// </para>
    /// <para>
    /// <strong>The composite foreign keys are the tenant boundary.</strong> Both
    /// the chunk table and the assignment table point at
    /// <c>(id, tenant_id)</c> rather than at <c>id</c> alone. A chunk filed under a
    /// workspace its document does not belong to cannot be written; nor can an
    /// assignment between one workspace's agent and another workspace's document,
    /// because the single <c>tenant_id</c> on the row would have to satisfy two
    /// foreign keys that disagree. This is isolation the database enforces, not
    /// isolation a handler remembers.
    /// </para>
    /// <para>
    /// <strong>No physical <c>xmin</c> column is created.</strong> The scaffolder
    /// emits one for every entity with the concurrency token mapped, and it is
    /// removed here exactly as it was in the five migrations before this. A real
    /// column of that name would shadow PostgreSQL's own system column and silently
    /// disable every precondition built on it -- every write would appear to
    /// succeed and no stale update would ever be caught. A schema test asserts its
    /// absence.
    /// </para>
    /// <para>
    /// Nothing is backfilled, because nothing existed. These are three new tables
    /// on a schema that had no knowledge at all.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class AddKnowledgeBaseAndVectorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Before anything that names the type it provides.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector");

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    indexed_fingerprint = table.Column<byte[]>(type: "bytea", nullable: true),
                    indexed_profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),

                    // xmin is deliberately NOT created here, exactly as in the five
                    // migrations before this one. It is PostgreSQL's own per-row
                    // system column and is mapped as a shadow concurrency token; a
                    // physical column of the same name would shadow it and break
                    // every optimistic-concurrency check on this table without any
                    // error to say so.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_documents", x => x.id);
                    table.UniqueConstraint("ak_knowledge_documents_id_tenant", x => new { x.id, x.tenant_id });
                    table.ForeignKey(
                        name: "fk_knowledge_documents_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_knowledge_documents",
                columns: table => new
                {
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    knowledge_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    // The primary key is what makes assigning the same document to
                    // the same agent twice impossible, rather than merely unusual.
                    table.PrimaryKey("pk_ai_agent_knowledge_documents", x => new { x.ai_agent_id, x.knowledge_document_id });
                    table.ForeignKey(
                        name: "fk_ai_agent_knowledge_documents_agent",
                        columns: x => new { x.ai_agent_id, x.tenant_id },
                        principalTable: "ai_agents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_agent_knowledge_documents_document",
                        columns: x => new { x.knowledge_document_id, x.tenant_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_agent_knowledge_documents_ai_agent_id_tenant_id",
                table: "ai_agent_knowledge_documents",
                columns: new[] { "ai_agent_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_agent_knowledge_documents_knowledge_document_id_tenant_id",
                table: "ai_agent_knowledge_documents",
                columns: new[] { "knowledge_document_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_tenant",
                table: "knowledge_documents",
                column: "tenant_id");

            // Index metadata is all three columns or none of them. A row holding a
            // fingerprint but no profile would be a document claiming to be indexed
            // by nothing in particular, and the readiness derivation would have to
            // guess what that meant.
            migrationBuilder.Sql(
                """
                ALTER TABLE knowledge_documents
                  ADD CONSTRAINT ck_knowledge_documents_index_complete CHECK (
                    (indexed_fingerprint IS NULL AND indexed_profile IS NULL
                     AND indexed_at IS NULL)
                    OR (indexed_fingerprint IS NOT NULL AND indexed_profile IS NOT NULL
                        AND indexed_at IS NOT NULL))
                """);

            // Written in SQL because this table is not part of the EF model. The
            // width is fixed at 1,536 to match the configured embedding profile:
            // vectors of different widths cannot be compared, so the column is what
            // makes a mismatch a refused insert rather than a silently meaningless
            // distance.
            migrationBuilder.Sql(
                """
                CREATE TABLE knowledge_chunks (
                    id uuid NOT NULL,
                    tenant_id uuid NOT NULL,
                    knowledge_document_id uuid NOT NULL,
                    ordinal integer NOT NULL,
                    content text NOT NULL,
                    embedding vector(1536) NOT NULL,
                    embedding_profile character varying(128) NOT NULL,
                    created_at timestamp with time zone NOT NULL,
                    CONSTRAINT pk_knowledge_chunks PRIMARY KEY (id),
                    CONSTRAINT ck_knowledge_chunks_ordinal CHECK (ordinal >= 0),
                    CONSTRAINT ux_knowledge_chunks_document_ordinal
                        UNIQUE (knowledge_document_id, ordinal),
                    CONSTRAINT fk_knowledge_chunks_document
                        FOREIGN KEY (knowledge_document_id, tenant_id)
                        REFERENCES knowledge_documents (id, tenant_id)
                        ON DELETE CASCADE
                )
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_knowledge_chunks_tenant_document
                    ON knowledge_chunks (tenant_id, knowledge_document_id)
                """);

            // HNSW with cosine distance, matching the operator the retrieval query
            // orders by. Cosine because embeddings encode direction rather than
            // magnitude: two passages saying the same thing at different lengths
            // should be near each other, and Euclidean distance would say they are
            // not. HNSW rather than IVFFlat because it needs no training pass over
            // existing data, which a workspace indexing its first document does not
            // have.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_knowledge_chunks_embedding
                    ON knowledge_chunks USING hnsw (embedding vector_cosine_ops)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Chunks first: they hold a foreign key into the documents table.
            migrationBuilder.Sql("DROP TABLE IF EXISTS knowledge_chunks");

            migrationBuilder.DropTable(
                name: "ai_agent_knowledge_documents");

            migrationBuilder.DropTable(
                name: "knowledge_documents");

            // The extension is deliberately left in place. Dropping it would remove
            // the type from the whole database, and this migration cannot know
            // whether something else has since come to depend on it.
        }
    }
}
