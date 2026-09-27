using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductionReadinessHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claim_token",
                table: "billing_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                table: "billing_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_index_attempts_in_flight",
                table: "knowledge_index_attempts",
                columns: new[] { "document_id", "tenant_id" },
                unique: true,
                filter: "status = 'Started'");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_turns_generating",
                table: "conversation_turns",
                column: "created_at",
                filter: "status = 'Generating'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_knowledge_index_attempts_in_flight",
                table: "knowledge_index_attempts");

            migrationBuilder.DropIndex(
                name: "ix_conversation_turns_generating",
                table: "conversation_turns");

            migrationBuilder.DropColumn(
                name: "claim_token",
                table: "billing_events");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "billing_events");
        }
    }
}
