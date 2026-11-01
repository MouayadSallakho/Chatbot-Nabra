using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationHandlingEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_handling_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    after_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_handling_events", x => x.id);
                    table.CheckConstraint("ck_conversation_handling_events_after_sequence", "after_sequence >= 0");
                    table.ForeignKey(
                        name: "fk_conversation_handling_events_actor_member",
                        columns: x => new { x.actor_user_id, x.tenant_id },
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "user_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversation_handling_events_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_handling_events_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_handling_events_conversation",
                table: "conversation_handling_events",
                columns: new[] { "conversation_id", "after_sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_handling_events");
        }
    }
}
