using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_reads",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_sequence = table.Column<long>(type: "bigint", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_reads", x => new { x.conversation_id, x.user_id });
                    table.CheckConstraint("ck_conversation_reads_last_read_sequence", "last_read_sequence >= 0");
                    table.ForeignKey(
                        name: "fk_conversation_reads_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_reads_member",
                        columns: x => new { x.user_id, x.tenant_id },
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "user_id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_reads_member",
                table: "conversation_reads",
                columns: new[] { "user_id", "tenant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_reads");
        }
    }
}
