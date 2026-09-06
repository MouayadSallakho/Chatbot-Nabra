using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRolesAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                    // xmin is deliberately NOT created here, exactly as in the two
                    // accepted migrations before this one. It is PostgreSQL's per-row
                    // transaction id system column: it exists on every table already, and
                    // CREATE TABLE rejects a user column of that name. The model maps it as a
                    // read-only concurrency token, so EF reads it and puts it in the UPDATE
                    // predicate; nothing needs to create it. The scaffolder cannot know that --
                    // Npgsql 10 still emits the column -- so its generated line is removed by
                    // hand after every regeneration, and MigrationSchemaTests fails if a future
                    // regeneration ever puts it back.
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_roles", x => x.id);

                    // Not a duplicate of the primary key, despite looking like one.
                    // PostgreSQL requires a unique constraint on the columns a foreign key
                    // references, and tenant_role_assignments references this exact pair.
                    // It is what forces an assignment's role and its membership to name the
                    // same tenant. See TenantRoleAssignmentConfiguration.
                    table.UniqueConstraint("ak_tenant_roles_id_tenant", x => new { x.id, x.tenant_id });

                    table.ForeignKey(
                        name: "fk_tenant_roles_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    tenant_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    // The composite primary key is where "a role cannot grant the same
                    // permission twice" lives. Structural, rather than a unique constraint
                    // beside a surrogate key that a later migration could drop.
                    table.PrimaryKey("PK_role_permissions", x => new { x.tenant_role_id, x.permission });

                    // Cascade, uniquely in this schema, and it is right here: a grant has no
                    // meaning without the role that carries it and is not business content
                    // anybody would want to recover.
                    table.ForeignKey(
                        name: "fk_role_permissions_role",
                        column: x => x.tenant_role_id,
                        principalTable: "tenant_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_role_assignments",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_role_assignments", x => new { x.user_id, x.tenant_id, x.tenant_role_id });

                    // The two composite foreign keys below are the whole point of this
                    // table carrying a tenant column. Both read this row's own tenant_id,
                    // so PostgreSQL accepts the row only when the membership and the role
                    // agree about which workspace they are in. A row pairing a membership
                    // in tenant A with a role from tenant B cannot be written at all.
                    table.ForeignKey(
                        name: "fk_tenant_role_assignments_membership",
                        columns: x => new { x.user_id, x.tenant_id },
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "user_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_role_assignments_role",
                        columns: x => new { x.tenant_role_id, x.tenant_id },
                        principalTable: "tenant_roles",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_role_assignments_role",
                table: "tenant_role_assignments",
                columns: new[] { "tenant_role_id", "tenant_id" });

            // At most one Owner per workspace, held by the database rather than by the
            // application alone. A partial unique index, so a workspace may have any
            // number of custom roles while a second Owner is refused outright.
            migrationBuilder.CreateIndex(
                name: "ux_tenant_roles_owner",
                table: "tenant_roles",
                column: "tenant_id",
                unique: true,
                filter: "kind = 'Owner'");

            // Role names are unique within a workspace, ignoring capitalisation, and
            // deliberately NOT globally: two businesses may each have a "Support agent".
            migrationBuilder.CreateIndex(
                name: "ux_tenant_roles_tenant_name",
                table: "tenant_roles",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);

            // ----------------------------------------------------------------------
            // Backfill: every existing workspace gets an Owner role, and every
            // existing member is given it.
            //
            // This preserves the accepted Phase 12 meaning of the data exactly. Under
            // Phase 12 a membership row granted full access to its workspace, with no
            // gradation; under Phase 13 full access is what the Owner role means. So
            // "every existing member becomes an Owner" is not a generous default --
            // it is the same authority those rows already carried, written down in the
            // new model.
            //
            // Doing nothing was the alternative, and it would have locked every
            // existing member out of every existing workspace: a member with no
            // assignments may do nothing, and no endpoint in this platform grants an
            // assignment. There is no production database yet, so in practice this
            // touches only development data -- but a migration that silently revoked
            // everyone's access would be wrong whether or not anybody was there to
            // notice.
            //
            // Both statements are set-based and run inside the migration's own
            // transaction, so a workspace either gains an Owner and its assignments or
            // the whole migration rolls back.
            // ----------------------------------------------------------------------
            migrationBuilder.Sql(
                """
                -- One Owner per existing tenant.
                --
                -- The id is a UUID version 7, matching every identifier this platform
                -- mints, rather than the version 4 that gen_random_uuid() returns on its
                -- own. PostgreSQL 17 has no uuidv7(), so the value is assembled: the
                -- random UUID supplies the entropy and the correct variant bits, the
                -- first 48 bits are overwritten with the current Unix time in
                -- milliseconds, and the version nibble is set to 7. Building it this way
                -- keeps the time-ordered insert locality that the whole schema relies on
                -- for its indexes, and keeps a backfilled row indistinguishable in shape
                -- from one the application would have written.
                --
                -- The name is the constant TenantRole.OwnerRoleName, and the normalised
                -- form is its ToUpperInvariant -- both ASCII, so no collation question
                -- arises here.
                INSERT INTO tenant_roles (id, tenant_id, name, normalized_name, kind, created_at, updated_at)
                SELECT
                    (overlay(
                        overlay(
                            overlay(gen_random_uuid()::text placing substr(stamp.hex, 1, 8) from 1 for 8)
                            placing substr(stamp.hex, 9, 4) from 10 for 4)
                        placing '7' from 15 for 1))::uuid,
                    tenant.id,
                    'Owner',
                    'OWNER',
                    'Owner',
                    now(),
                    now()
                FROM tenants AS tenant
                CROSS JOIN (
                    SELECT lpad(to_hex((extract(epoch FROM now()) * 1000)::bigint), 12, '0') AS hex
                ) AS stamp;

                -- Every existing member holds it. The join is on tenant_id, so a member
                -- can only ever receive the Owner role of the workspace they already
                -- belong to -- which is also the only row the composite foreign keys
                -- would accept.
                INSERT INTO tenant_role_assignments (user_id, tenant_id, tenant_role_id, created_at)
                SELECT membership.user_id, membership.tenant_id, owner.id, now()
                FROM tenant_memberships AS membership
                JOIN tenant_roles AS owner
                    ON owner.tenant_id = membership.tenant_id
                    AND owner.kind = 'Owner';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The backfilled rows go with the tables that hold them, so there is no
            // hand-written delete to keep in step. Assignments are dropped before the
            // roles they reference, and role_permissions before that.
            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "tenant_role_assignments");

            migrationBuilder.DropTable(
                name: "tenant_roles");
        }
    }
}
