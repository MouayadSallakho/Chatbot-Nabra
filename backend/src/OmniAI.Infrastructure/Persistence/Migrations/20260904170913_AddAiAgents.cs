using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instructions = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_ai_agents", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_agents_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agents_tenant",
                table: "ai_agents",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_agents");
        }
    }
}
