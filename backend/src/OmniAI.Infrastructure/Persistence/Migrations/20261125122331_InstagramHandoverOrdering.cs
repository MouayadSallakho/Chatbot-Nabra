using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstagramHandoverOrdering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_handover_timestamp",
                table: "instagram_simulator_threads",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Older manually switched threads have a known current owner but no recorded event time.
            migrationBuilder.Sql("""
                UPDATE instagram_simulator_threads
                SET last_handover_timestamp = floor(extract(epoch FROM statement_timestamp()) * 1000)::bigint
                WHERE ownership_version > 0
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_handover_timestamp",
                table: "instagram_simulator_threads");
        }
    }
}
