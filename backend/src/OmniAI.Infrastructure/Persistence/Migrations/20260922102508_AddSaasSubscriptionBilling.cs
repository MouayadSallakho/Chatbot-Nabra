using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Six tables, one plan, and not a single subscription invented for anybody.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>What is deliberately absent.</strong> No wallet, no credits, no
    /// meters, no consumption invoices, no payment transactions, no tax rows and
    /// no card data of any kind. This platform sells a fixed recurring
    /// subscription to a plan; everything a payment provider is better at
    /// remains theirs, and a column that does not exist cannot leak.
    /// </para>
    /// <para>
    /// <strong>Stripe does not decide entitlements.</strong> A subscription row
    /// records what a provider says; Phase 24's
    /// <c>workspace_plan_assignments</c> remains the single thing every
    /// entitlement check reads. That is why there is no price on a plan and no
    /// limit on an offer: <c>billing_plan_offers</c> says what a plan costs,
    /// <c>plans</c> says what it allows, and neither can quietly become the
    /// other.
    /// </para>
    /// <para>
    /// <strong>Money is a whole number of minor units.</strong> 19.99 has no
    /// exact binary floating-point representation, and a price that drifts in the
    /// second decimal is a price somebody is eventually charged wrongly. The
    /// currency sits beside it and amounts in different currencies are never
    /// added.
    /// </para>
    /// <para>
    /// <strong>No offers are seeded.</strong> A price is a commercial decision
    /// and a provider identifier is deployment-specific; inventing either here
    /// would put a fictional price in every production database and a Stripe id
    /// that resolves to nothing. A deployment with no offers shows none and
    /// refuses checkout, which is the correct behaviour rather than a gap.
    /// </para>
    /// <para>
    /// <strong>No subscription is fabricated for existing workspaces.</strong>
    /// Nobody who has not bought anything gets a row saying they did. Workspaces
    /// created before this migration keep the plan assignment Phase 24 gave them
    /// and are entirely unaffected.
    /// </para>
    /// <para>
    /// <strong>The seeded <c>free</c> plan exists to be fallen back to.</strong>
    /// When a subscription ends or is suspended, a workspace has to land
    /// somewhere finite; <c>legacy</c> and <c>development</c> are unlimited and
    /// would turn non-payment into an upgrade. Its limits are small and real, and
    /// a downgrade onto it destroys nothing — existing members, agents, documents
    /// and channels remain, and only new creation is refused while usage sits
    /// above the limit.
    /// </para>
    /// </remarks>
    public partial class AddSaasSubscriptionBilling : Migration
    {
        /// <summary>
        /// The commercial floor a lapsed workspace lands on.
        /// </summary>
        /// <remarks>
        /// A fixed identifier rather than a generated one, so
        /// <c>Billing:FallbackPlanCode</c> resolves to the same plan in every
        /// database and the down migration can remove exactly what went in.
        /// </remarks>
        private static readonly Guid FreePlanId =
            new("019995b8-0000-7000-8000-00000000f8ee");
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_checkout_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_checkout_intents", x => x.id);
                    table.ForeignKey(
                        name: "FK_billing_checkout_intents_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_subscription_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "billing_plan_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    trial_days = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_plan_offers", x => x.id);
                    table.CheckConstraint("ck_billing_plan_offers_amount_not_negative", "\"amount_minor_units\" >= 0");
                    table.CheckConstraint("ck_billing_plan_offers_interval", "\"interval\" IN ('Monthly', 'Annual')");
                    table.CheckConstraint("ck_billing_plan_offers_trial_days", "\"trial_days\" IS NULL OR (\"trial_days\" >= 0 AND \"trial_days\" <= 365)");
                    table.ForeignKey(
                        name: "FK_billing_plan_offers_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_billing_customers",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_billing_customers", x => new { x.tenant_id, x.provider });
                    table.ForeignKey(
                        name: "FK_workspace_billing_customers_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "billing_provider_price_mappings",
                columns: table => new
                {
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_price_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_provider_price_mappings", x => new { x.offer_id, x.provider });
                    table.ForeignKey(
                        name: "FK_billing_provider_price_mappings_billing_plan_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "billing_plan_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspace_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    offer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    interval = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_subscription_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    current_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    canceled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grace_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_workspace_subscriptions_billing_plan_offers_offer_id",
                        column: x => x.offer_id,
                        principalTable: "billing_plan_offers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_billing_checkout_intents_tenant_id",
                table: "billing_checkout_intents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_billing_checkout_intents_idempotency",
                table: "billing_checkout_intents",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_billing_checkout_intents_provider_session",
                table: "billing_checkout_intents",
                columns: new[] { "provider", "provider_session_id" },
                unique: true,
                filter: "\"provider_session_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_billing_events_status_received",
                table: "billing_events",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ux_billing_events_provider_event",
                table: "billing_events",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_billing_plan_offers_plan_id",
                table: "billing_plan_offers",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ux_billing_plan_offers_code",
                table: "billing_plan_offers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_billing_price_mappings_provider_price",
                table: "billing_provider_price_mappings",
                columns: new[] { "provider", "external_price_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workspace_billing_customers_provider_customer",
                table: "workspace_billing_customers",
                columns: new[] { "provider", "provider_customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_subscriptions_offer_id",
                table: "workspace_subscriptions",
                column: "offer_id");

            migrationBuilder.CreateIndex(
                name: "ux_workspace_subscriptions_current",
                table: "workspace_subscriptions",
                column: "tenant_id",
                unique: true,
                filter: "\"status\" <> 'Canceled'");

            migrationBuilder.CreateIndex(
                name: "ux_workspace_subscriptions_provider_subscription",
                table: "workspace_subscriptions",
                columns: new[] { "provider", "provider_subscription_id" },
                unique: true,
                filter: "\"provider_subscription_id\" IS NOT NULL");

            Seed(migrationBuilder);
        }

        /// <summary>
        /// Adds the free plan, if a deployment does not already have one.
        /// </summary>
        /// <remarks>
        /// Guarded, because <c>plans.code</c> is unique and a deployment may have
        /// added its own <c>free</c> already. Taking theirs rather than failing
        /// the migration is right: the code is what configuration names, and a
        /// plan somebody deliberately created is a better answer than one this
        /// file invented.
        /// </remarks>
        private static void Seed(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                $"""
                 INSERT INTO plans (
                     id, code, display_name,
                     max_members, max_ai_agents, max_knowledge_documents,
                     max_web_chat_endpoints, max_whatsapp_connections,
                     max_monthly_provider_operations, created_at)
                 VALUES (
                     '{FreePlanId}', 'free', 'Free',
                     2, 1, 10, 1, 0, 200, TIMESTAMPTZ '2026-09-22 00:00:00+00')
                 ON CONFLICT (code) DO NOTHING
                 """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "billing_checkout_intents");

            migrationBuilder.DropTable(
                name: "billing_events");

            migrationBuilder.DropTable(
                name: "billing_provider_price_mappings");

            migrationBuilder.DropTable(
                name: "workspace_billing_customers");

            migrationBuilder.DropTable(
                name: "workspace_subscriptions");

            migrationBuilder.DropTable(
                name: "billing_plan_offers");

            // Only the row this migration inserted, and only while nothing is on
            // it. A workspace assigned to the free plan means somebody is using
            // it, and removing it underneath them would leave a workspace with no
            // plan -- which the entitlement service refuses outright.
            migrationBuilder.Sql(
                $"""
                 DELETE FROM plans
                  WHERE id = '{FreePlanId}'
                    AND NOT EXISTS (
                        SELECT 1
                          FROM workspace_plan_assignments
                         WHERE plan_id = '{FreePlanId}')
                 """);
        }
    }
}
