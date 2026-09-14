using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Where a file-backed knowledge document's text came from.
    /// </summary>
    /// <remarks>
    /// Two hand-written departures from what the scaffolder produced, recorded
    /// here because each is the kind of thing a reviewer should be able to check
    /// rather than take on trust.
    /// <para>
    /// <strong>1. The scaffolder's <c>xmin</c> column is removed.</strong> Same
    /// as every other table on this platform: it is PostgreSQL's own system
    /// column, it already exists, and creating it is both unnecessary and
    /// rejected by <c>CREATE TABLE</c>.
    /// </para>
    /// <para>
    /// <strong>2. The document foreign key is composite, and written by
    /// hand.</strong> EF cannot express <c>(knowledge_document_id, tenant_id)</c>
    /// into <c>knowledge_documents (id, tenant_id)</c> without modelling a
    /// navigation the Domain has no business carrying. The single-column
    /// alternative would let a source row name a document belonging to another
    /// workspace, leaving an application check as the only thing between that and
    /// one company's file metadata attached to another company's document. This
    /// makes it unrepresentable. The alternate key it points at,
    /// <c>ak_knowledge_documents_id_tenant</c>, already exists from Phase 17 —
    /// the chunk table uses it for the same reason.
    /// </para>
    /// <para>
    /// <strong>There is deliberately no column for the file itself.</strong> No
    /// path, no bytes, no storage key. An upload is read during the request and
    /// discarded; what persists is this metadata beside the document's text.
    /// </para>
    /// </remarks>
    public partial class AddKnowledgeFileSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "knowledge_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    knowledge_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    // xmin is deliberately NOT created here. It is PostgreSQL's per-row
                    // transaction id system column: it exists on every table already, and
                    // CREATE TABLE rejects a user column of that name. The model maps it as a
                    // read-only concurrency token, so EF reads it and puts it in the UPDATE
                    // predicate; nothing needs to create it. The scaffolder cannot know that --
                    // Npgsql 10 still emits the column -- so its generated line is removed by
                    // hand after every regeneration, and MigrationSchemaTests fails if a future
                    // regeneration ever puts it back.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_sources_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sources_tenant",
                table: "knowledge_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_sources_document",
                table: "knowledge_sources",
                column: "knowledge_document_id",
                unique: true);

            // <strong>The workspace travels with the reference.</strong> A source
            // may only point at a document in its own workspace, and the pair is
            // what the database checks -- so a row naming somebody else's
            // document cannot be written at all, rather than being prevented by a
            // line of application code somebody could later move.
            //
            // Cascading, because a source describes one document and has no
            // meaning without it.
            migrationBuilder.Sql(
                """
                ALTER TABLE knowledge_sources
                  ADD CONSTRAINT fk_knowledge_sources_document
                  FOREIGN KEY (knowledge_document_id, tenant_id)
                  REFERENCES knowledge_documents (id, tenant_id)
                  ON DELETE CASCADE
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The hand-written constraint needs no line of its own: dropping the
        /// table drops it.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "knowledge_sources");
        }
    }
}
