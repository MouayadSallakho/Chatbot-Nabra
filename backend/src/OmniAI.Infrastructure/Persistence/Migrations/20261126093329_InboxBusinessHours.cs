using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InboxBusinessHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_schedules",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    minutes = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_schedules", x => x.tenant_id);
                    table.CheckConstraint("ck_inbox_schedule_week", "cardinality(minutes) = 14");
                    table.ForeignKey(
                        name: "FK_inbox_schedules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inbox_after_hours_teams",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_after_hours_teams", x => x.tenant_id);
                    table.ForeignKey(
                        name: "FK_inbox_after_hours_teams_inbox_schedules_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "inbox_schedules",
                        principalColumn: "tenant_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inbox_after_hours_teams_tenant_roles_role_id_tenant_id",
                        columns: x => new { x.role_id, x.tenant_id },
                        principalTable: "tenant_roles",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_after_hours_teams_role_id_tenant_id",
                table: "inbox_after_hours_teams",
                columns: new[] { "role_id", "tenant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_after_hours_teams");

            migrationBuilder.DropTable(
                name: "inbox_schedules");
        }
    }
}
