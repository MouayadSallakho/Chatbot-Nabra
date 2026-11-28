using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirstReplyTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "first_reply_target_minutes",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_first_reply_target",
                table: "tenants",
                sql: "first_reply_target_minutes BETWEEN 1 AND 1440");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_first_reply_target",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "first_reply_target_minutes",
                table: "tenants");
        }
    }
}
