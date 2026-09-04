using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "message_ingestion_sequence");

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
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
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    table.PrimaryKey("PK_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversations_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ingestion_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_last_activity",
                table: "conversations",
                columns: new[] { "tenant_id", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_ingestion",
                table: "messages",
                columns: new[] { "conversation_id", "ingestion_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropSequence(
                name: "message_ingestion_sequence");
        }
    }
}
