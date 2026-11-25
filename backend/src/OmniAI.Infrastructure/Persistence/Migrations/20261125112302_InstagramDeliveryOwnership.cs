using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstagramDeliveryOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "external_owner",
                table: "instagram_simulator_threads",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ownership_version",
                table: "instagram_simulator_threads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The previous Instagram simulator exposed these replies as soon as they were written.
            // Preserve their customer-visible history when enabling explicit collection.
            migrationBuilder.Sql("""
                INSERT INTO messenger_simulator_receipts (message_id, received_at, status)
                SELECT m.id, m.sent_at, 'Confirmed' FROM messages AS m
                JOIN instagram_simulator_threads AS t ON t.conversation_id = m.conversation_id
                WHERE m.sender IN ('AiAgent', 'HumanAgent')
                ON CONFLICT (message_id) DO NOTHING
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_owner",
                table: "instagram_simulator_threads");

            migrationBuilder.DropColumn(
                name: "ownership_version",
                table: "instagram_simulator_threads");
        }
    }
}
