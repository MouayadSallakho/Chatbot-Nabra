using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebChatGreetingPerLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "widget_greeting_ar",
                table: "web_chat_endpoints",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "widget_greeting_en",
                table: "web_chat_endpoints",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "widget_greeting_ar",
                table: "web_chat_endpoints");

            migrationBuilder.DropColumn(
                name: "widget_greeting_en",
                table: "web_chat_endpoints");
        }
    }
}
