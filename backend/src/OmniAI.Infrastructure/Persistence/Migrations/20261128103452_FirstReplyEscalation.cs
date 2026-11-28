using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirstReplyEscalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "escalation_delay_minutes",
                table: "tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_escalation_delay",
                table: "tenants",
                sql: "escalation_delay_minutes BETWEEN 0 AND 1440");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_escalation_delay",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "escalation_delay_minutes",
                table: "tenants");
        }
    }
}
