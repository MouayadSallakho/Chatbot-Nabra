using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReliabilityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ai_provider_usages",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    friendly_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_index_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_index_attempts", x => x.id);
                    table.CheckConstraint("ck_knowledge_index_attempts_settled", "(status = 'Started') = (completed_at IS NULL)");
                    table.CheckConstraint("ck_knowledge_index_attempts_status", "status IN ('Started', 'Succeeded', 'Failed', 'Unknown')");
                    table.ForeignKey(
                        name: "fk_knowledge_index_attempts_document",
                        columns: x => new { x.document_id, x.tenant_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_usages_open",
                table: "ai_provider_usages",
                column: "started_at",
                filter: "status = 'Started'");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_index_attempts_document",
                table: "knowledge_index_attempts",
                columns: new[] { "document_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_index_attempts_started",
                table: "knowledge_index_attempts",
                column: "started_at",
                filter: "status = 'Started'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "knowledge_index_attempts");

            migrationBuilder.DropIndex(
                name: "ix_ai_provider_usages_open",
                table: "ai_provider_usages");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ai_provider_usages");
        }
    }
}
