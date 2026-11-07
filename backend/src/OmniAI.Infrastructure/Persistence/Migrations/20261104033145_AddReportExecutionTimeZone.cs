using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportExecutionTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                table: "report_executions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "time_zone",
                table: "report_executions");
        }
    }
}
