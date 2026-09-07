using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteChatAndConversationRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_ai_agents_id_tenant",
                table: "ai_agents",
                columns: new[] { "id", "tenant_id" });

            migrationBuilder.CreateTable(
                name: "conversation_turns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reply_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_turns", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_turns_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_turns_customer_message",
                        column: x => x.customer_message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "web_chat_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)

                    // xmin is deliberately NOT created here, exactly as in the four
                    // migrations before this one. It is PostgreSQL's own per-row
                    // transaction id -- a system column present on every table ever
                    // created -- which the model maps as a shadow concurrency token
                    // so a web chat endpoint's ETag has something to be built from.
                    // The scaffolder cannot tell a system column from one this
                    // platform owns and emitted an AddColumn for it; creating a real
                    // "xmin" column would shadow the system one and break the
                    // concurrency check it exists to serve.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_chat_endpoints", x => x.id);
                    table.UniqueConstraint("ak_web_chat_endpoints_id_tenant", x => new { x.id, x.tenant_id });
                    table.ForeignKey(
                        name: "fk_web_chat_endpoints_agent",
                        columns: x => new { x.ai_agent_id, x.tenant_id },
                        principalTable: "ai_agents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_web_chat_endpoints_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "web_chat_sessions",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    web_chat_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_chat_sessions", x => x.conversation_id);
                    table.ForeignKey(
                        name: "fk_web_chat_sessions_agent",
                        columns: x => new { x.ai_agent_id, x.tenant_id },
                        principalTable: "ai_agents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_web_chat_sessions_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_web_chat_sessions_endpoint",
                        columns: x => new { x.web_chat_endpoint_id, x.tenant_id },
                        principalTable: "web_chat_endpoints",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_conversation_turns_client_message",
                table: "conversation_turns",
                columns: new[] { "conversation_id", "client_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_conversation_turns_customer_message",
                table: "conversation_turns",
                column: "customer_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_endpoints_ai_agent_id_tenant_id",
                table: "web_chat_endpoints",
                columns: new[] { "ai_agent_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_web_chat_endpoints_public_key",
                table: "web_chat_endpoints",
                column: "public_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_web_chat_endpoints_tenant_agent",
                table: "web_chat_endpoints",
                columns: new[] { "tenant_id", "ai_agent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_sessions_ai_agent_id_tenant_id",
                table: "web_chat_sessions",
                columns: new[] { "ai_agent_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_sessions_web_chat_endpoint_id_tenant_id",
                table: "web_chat_sessions",
                columns: new[] { "web_chat_endpoint_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_web_chat_sessions_access_token_hash",
                table: "web_chat_sessions",
                column: "access_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_turns");

            migrationBuilder.DropTable(
                name: "web_chat_sessions");

            migrationBuilder.DropTable(
                name: "web_chat_endpoints");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_ai_agents_id_tenant",
                table: "ai_agents");
        }
    }
}
