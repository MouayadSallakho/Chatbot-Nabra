using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Two tables, three plans, and an assignment for every workspace that
    /// already exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>What is missing is the design.</strong> No invoice table, no
    /// subscription, no credits, no wallet, no billing transactions and no daily
    /// quota rollup. Monthly provider consumption is counted from Phase 23's
    /// <c>ai_provider_usages</c>, which stays the single authority — a second
    /// counter would be a second truth, and the two would disagree the first time
    /// a write path forgot one of them.
    /// </para>
    /// <para>
    /// <strong>Every limit column is nullable, and null means unlimited.</strong>
    /// A sentinel such as <c>-1</c> or <c>2147483647</c> would have to be
    /// understood identically by the database, every query and every screen, and
    /// would be summed by the first report that forgot. The check constraints keep
    /// finite limits at or above zero.
    /// </para>
    /// <para>
    /// <strong>Existing workspaces are backfilled onto a legacy plan with no
    /// limits.</strong> They were created under a platform that had none, and
    /// imposing one retrospectively would break working workspaces — including
    /// every development and test database — for a commercial decision nobody has
    /// taken yet. New workspaces do not inherit it: onboarding uses the plan named
    /// in configuration, and refuses to create a workspace when none is named.
    /// </para>
    /// <para>
    /// <strong>The seeded plans carry no price.</strong> What a plan costs is
    /// Phase 25's subject; there is no column here to put it in.
    /// </para>
    /// </remarks>
    public partial class AddWorkspacePlansAndEntitlements : Migration
    {
        /// <summary>
        /// The plan every pre-existing workspace is put on.
        /// </summary>
        /// <remarks>
        /// Fixed identities rather than generated ones, so re-running this
        /// migration against a database that already has them is a conflict the
        /// primary key catches rather than a duplicate set of plans.
        /// </remarks>
        private static readonly Guid LegacyPlanId =
            new("01a12000-0000-7000-8000-000000000001");

        private static readonly Guid DevelopmentPlanId =
            new("01a12000-0000-7000-8000-000000000002");

        private static readonly Guid StarterPlanId =
            new("01a12000-0000-7000-8000-000000000003");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_members = table.Column<int>(type: "integer", nullable: true),
                    max_ai_agents = table.Column<int>(type: "integer", nullable: true),
                    max_knowledge_documents = table.Column<int>(type: "integer", nullable: true),
                    max_web_chat_endpoints = table.Column<int>(type: "integer", nullable: true),
                    max_whatsapp_connections = table.Column<int>(type: "integer", nullable: true),
                    max_monthly_provider_operations = table.Column<int>(type: "integer", nullable: true),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                    table.CheckConstraint("ck_plans_max_ai_agents_not_negative", "\"max_ai_agents\" IS NULL OR \"max_ai_agents\" >= 0");
                    table.CheckConstraint("ck_plans_max_knowledge_documents_not_negative", "\"max_knowledge_documents\" IS NULL OR \"max_knowledge_documents\" >= 0");
                    table.CheckConstraint("ck_plans_max_members_not_negative", "\"max_members\" IS NULL OR \"max_members\" >= 0");
                    table.CheckConstraint("ck_plans_max_monthly_provider_operations_not_negative", "\"max_monthly_provider_operations\" IS NULL OR \"max_monthly_provider_operations\" >= 0");
                    table.CheckConstraint("ck_plans_max_web_chat_endpoints_not_negative", "\"max_web_chat_endpoints\" IS NULL OR \"max_web_chat_endpoints\" >= 0");
                    table.CheckConstraint("ck_plans_max_whatsapp_connections_not_negative", "\"max_whatsapp_connections\" IS NULL OR \"max_whatsapp_connections\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "workspace_plan_assignments",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_plan_assignments", x => x.tenant_id);
                    table.ForeignKey(
                        name: "FK_workspace_plan_assignments_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_plan_assignments_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_plan_assignments_plan_id",
                table: "workspace_plan_assignments",
                column: "plan_id");

            Seed(migrationBuilder);
            Backfill(migrationBuilder);
        }

        /// <summary>
        /// The plans this platform ships with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three, and each exists for a reason. <c>legacy</c> is what workspaces
        /// that predate plans are on: no limits, because they were built without
        /// any and must keep working. <c>development</c> is what a development or
        /// test deployment names in configuration — also unlimited, so a developer
        /// is never blocked by a commercial decision while writing code.
        /// <c>starter</c> is a real, finite plan: it exists so a production
        /// deployment has something honest to name, and so that the limits are
        /// exercised by something other than a test.
        /// </para>
        /// <para>
        /// The numbers on <c>starter</c> are a starting point for this platform to
        /// adjust, not a commercial commitment. Changing what a plan means for the
        /// workspaces already on it is deliberately not possible by editing this
        /// migration: a new plan is added and workspaces are moved.
        /// </para>
        /// </remarks>
        private static void Seed(MigrationBuilder migrationBuilder)
        {
            string[] columns =
            [
                "id", "code", "display_name",
                "max_members", "max_ai_agents", "max_knowledge_documents",
                "max_web_chat_endpoints", "max_whatsapp_connections",
                "max_monthly_provider_operations", "created_at",
            ];

            DateTimeOffset created = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

            migrationBuilder.InsertData(
                table: "plans",
                columns: columns,
                values: new object[,]
                {
                    // Unlimited everywhere: null, never a large number.
                    {
                        LegacyPlanId, "legacy", "Legacy (unlimited)",
                        null, null, null, null, null, null, created,
                    },
                    {
                        DevelopmentPlanId, "development", "Development (unlimited)",
                        null, null, null, null, null, null, created,
                    },
                    {
                        StarterPlanId, "starter", "Starter",
                        5, 3, 25, 1, 1, 2000, created,
                    },
                });
        }

        /// <summary>
        /// Puts every workspace that already exists onto the legacy plan.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Set-based and complete.</strong> Every row in <c>tenants</c>
        /// gets an assignment, so no workspace is left without one — a workspace
        /// with no plan is refused by the entitlement service rather than treated
        /// as unlimited, which would be the dangerous guess.
        /// </para>
        /// <para>
        /// <c>assigned_at</c> is taken from the workspace's earliest membership,
        /// which is the moment its owner was created alongside it. Truer than the
        /// instant this migration happened to run: these workspaces have operated
        /// under these (absent) limits since they existed, and stamping them all
        /// with one deployment timestamp would suggest something changed for them
        /// that day. A workspace with no membership at all falls back to the
        /// migration's own instant, because there is nothing better to say.
        /// </para>
        /// </remarks>
        private static void Backfill(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                $"""
                INSERT INTO workspace_plan_assignments (tenant_id, plan_id, assigned_at)
                SELECT
                    t.id,
                    '{LegacyPlanId}',
                    COALESCE(
                        (SELECT MIN(m.created_at)
                         FROM tenant_memberships m
                         WHERE m.tenant_id = t.id),
                        NOW())
                FROM tenants t
                ON CONFLICT (tenant_id) DO NOTHING;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_plan_assignments");

            migrationBuilder.DropTable(
                name: "plans");
        }
    }
}
