using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationHandlingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_handling_activity",
                table: "conversations",
                columns: new[] { "tenant_id", "handling", "last_activity_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_conversations_tenant_handling_activity",
                table: "conversations");
        }
    }
}
