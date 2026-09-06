using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityAndTenantMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_memberships", x => new { x.user_id, x.tenant_id });
                    table.ForeignKey(
                        name: "fk_tenant_memberships_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_user",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_user",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_user",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_tenant",
                table: "tenant_memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            // Added by hand, and it has to be: EF Core will not model a
            // relationship whose two sides disagree on their CLR type, and this one
            // joins a UserId value object to IdentityUser<Guid>.Id, a framework type
            // this platform does not own. See TenantMembershipConfiguration for the
            // alternatives that were considered and why each was worse.
            //
            // The constraint itself is entirely ordinary: PostgreSQL does not care
            // which tool emitted it, and referential integrity is enforced the same
            // way as for every other foreign key in this schema. It is placed after
            // both CreateTable calls because tenant_memberships is created before
            // users.
            //
            // RESTRICT, not CASCADE. Deleting an account must not silently delete
            // the memberships that make its workspaces reachable; doing so would
            // strand every tenant and every AI agent behind them, alive in the
            // database and reachable by nobody. What should happen to a workspace
            // when its last member leaves is an unanswered product question, and
            // this constraint keeps it unanswered rather than answering it by
            // accident.
            //
            // TenantMembershipSchemaTests asserts this constraint exists on the
            // applied schema with this delete rule, so a regeneration that dropped
            // this block would fail rather than quietly ship a table with no
            // referential integrity on its user column.
            migrationBuilder.AddForeignKey(
                name: "fk_tenant_memberships_user",
                table: "tenant_memberships",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // tenant_memberships goes first, which takes fk_tenant_memberships_user
            // with it, so the hand-added constraint needs no hand-added drop and
            // users can be dropped last with nothing still referencing it.
            migrationBuilder.DropTable(
                name: "tenant_memberships");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
