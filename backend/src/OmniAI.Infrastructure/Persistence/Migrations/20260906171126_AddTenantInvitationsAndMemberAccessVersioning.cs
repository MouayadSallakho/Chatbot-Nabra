using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantInvitationsAndMemberAccessVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // When a member's access last changed. Added nullable, backfilled, then
            // made required -- rather than taking the scaffolded default of
            // 0001-01-01, which would have told every existing workspace that
            // nobody's access had ever been set.
            //
            // Backfilled from created_at because that is when their access was in
            // fact last decided: a membership nobody has reassigned has held the
            // same roles since the day it was granted. It invents no moment that
            // did not happen.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "tenant_memberships",
                type: "timestamp with time zone",
                nullable: true);

            // Written as SQL rather than through AlterColumn: the scaffolder's
            // version compares against a column definition it infers, and without
            // the previous state spelled out it produced no NOT NULL at all --
            // leaving a column the model believes is required and the database
            // does not.
            migrationBuilder.Sql(
                """
                UPDATE tenant_memberships SET updated_at = created_at WHERE updated_at IS NULL;
                ALTER TABLE tenant_memberships ALTER COLUMN updated_at SET NOT NULL;
                """);

            // xmin is deliberately NOT created here, exactly as in the three
            // migrations before this one. It is PostgreSQL's own per-row
            // transaction id -- a system column present on every table ever
            // created -- which the model maps as a shadow concurrency token so a
            // member's ETag has something to be built from. The scaffolder cannot
            // tell a system column from one this platform owns and emitted an
            // AddColumn for it; creating a real "xmin" column would shadow the
            // system one and break the concurrency check it exists to serve.
            //
            // The matching DropColumn is absent from Down() for the same reason:
            // there is nothing to drop.

            migrationBuilder.CreateTable(
                name: "tenant_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    canonical_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_invitations", x => x.id);
                    table.UniqueConstraint("ak_tenant_invitations_id_tenant", x => new { x.id, x.tenant_id });
                    table.ForeignKey(
                        name: "fk_tenant_invitations_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invitation_roles",
                columns: table => new
                {
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation_roles", x => new { x.invitation_id, x.tenant_role_id });
                    table.ForeignKey(
                        name: "fk_invitation_roles_invitation_tenant",
                        columns: x => new { x.invitation_id, x.tenant_id },
                        principalTable: "tenant_invitations",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitation_roles_role",
                        columns: x => new { x.tenant_role_id, x.tenant_id },
                        principalTable: "tenant_roles",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invitation_roles_invitation_id_tenant_id",
                table: "invitation_roles",
                columns: new[] { "invitation_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_roles_role",
                table: "invitation_roles",
                columns: new[] { "tenant_role_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_invitations_tenant",
                table: "tenant_invitations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_invitations_open",
                table: "tenant_invitations",
                columns: new[] { "tenant_id", "canonical_email" },
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_invitations_token_hash",
                table: "tenant_invitations",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitation_roles");

            migrationBuilder.DropTable(
                name: "tenant_invitations");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "tenant_memberships");
        }
    }
}
