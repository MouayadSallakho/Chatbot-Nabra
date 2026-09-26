using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateSequence(
                name: "message_ingestion_sequence");

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
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    friendly_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                });

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
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
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
                name: "ai_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    instructions = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_agents", x => x.id);
                    table.UniqueConstraint("ak_ai_agents_id_tenant", x => new { x.id, x.tenant_id });
                    table.ForeignKey(
                        name: "fk_ai_agents_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_provider_usages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    operation_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    resolved_model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    cached_input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    output_tokens = table.Column<long>(type: "bigint", nullable: true),
                    reasoning_tokens = table.Column<long>(type: "bigint", nullable: true),
                    total_tokens = table.Column<long>(type: "bigint", nullable: true),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    input_rate_per_million = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    cached_input_rate_per_million = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    output_rate_per_million = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    rates_effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_provider_usages", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_provider_usages_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    merged_into_customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    merged_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                    table.UniqueConstraint("AK_customers_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_customers_merged_into",
                        column: x => x.merged_into_customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_customers_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    indexed_fingerprint = table.Column<byte[]>(type: "bytea", nullable: true),
                    indexed_profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    content_fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_documents", x => x.id);
                    table.UniqueConstraint("ak_knowledge_documents_id_tenant", x => new { x.id, x.tenant_id });
                    table.CheckConstraint("ck_knowledge_documents_index_complete", "(indexed_fingerprint IS NULL AND indexed_profile IS NULL AND indexed_at IS NULL) OR (indexed_fingerprint IS NOT NULL AND indexed_profile IS NOT NULL AND indexed_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_knowledge_documents_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_occurrences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurrence_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    claim_token = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_occurrences", x => x.id);
                    table.CheckConstraint("ck_notification_occurrences_attempts", "attempts >= 0");
                    table.ForeignKey(
                        name: "fk_notification_occurrences_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email_reports = table.Column<bool>(type: "boolean", nullable: false),
                    email_usage = table.Column<bool>(type: "boolean", nullable: false),
                    email_billing = table.Column<bool>(type: "boolean", nullable: false),
                    email_knowledge = table.Column<bool>(type: "boolean", nullable: false),
                    email_channel_health = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => new { x.tenant_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_notification_preferences_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_preferences_user",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    action_path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    deduplication_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.UniqueConstraint("AK_notifications_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_notifications_action_path", "action_path IS NULL OR action_path ~ '^/([a-z0-9-]+(/[a-z0-9-]+)*)?$'");
                    table.CheckConstraint("ck_notifications_expiry", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_notifications_recipient_user",
                        column: x => x.recipient_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    report_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disabled_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_schedules", x => x.id);
                    table.UniqueConstraint("AK_report_schedules_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.ForeignKey(
                        name: "fk_report_schedules_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "tenant_memberships",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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
                    table.ForeignKey(
                        name: "fk_tenant_memberships_user",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_roles", x => x.id);
                    table.UniqueConstraint("ak_tenant_roles_id_tenant", x => new { x.id, x.tenant_id });
                    table.ForeignKey(
                        name: "fk_tenant_roles_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
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

            migrationBuilder.CreateTable(
                name: "web_chat_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_key = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    widget_accent_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    widget_greeting = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    widget_launcher_position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    widget_theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_chat_endpoints", x => x.id);
                    table.UniqueConstraint("ak_web_chat_endpoints_id_tenant", x => new { x.id, x.tenant_id });
                    table.ForeignKey(
                        name: "fk_web_chat_endpoints_agent",
                        columns: x => new { x.ai_agent_id, x.tenant_id },
                        principalTable: "ai_agents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_web_chat_endpoints_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    waba_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phone_number_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    has_credential = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_whatsapp_connections_agent",
                        column: x => x.ai_agent_id,
                        principalTable: "ai_agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_whatsapp_connections_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_customer_identities",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    whatsapp_customer_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_customer_identities", x => new { x.tenant_id, x.whatsapp_customer_id });
                    table.ForeignKey(
                        name: "fk_whatsapp_customer_identities_customer",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_whatsapp_customer_identities_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ai_agent_knowledge_documents",
                columns: table => new
                {
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    knowledge_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_agent_knowledge_documents", x => new { x.ai_agent_id, x.knowledge_document_id });
                    table.ForeignKey(
                        name: "fk_ai_agent_knowledge_documents_agent",
                        columns: x => new { x.ai_agent_id, x.tenant_id },
                        principalTable: "ai_agents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ai_agent_knowledge_documents_document",
                        columns: x => new { x.knowledge_document_id, x.tenant_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    knowledge_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    embedding = table.Column<string>(type: "vector(1536)", nullable: false),
                    embedding_profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_chunks", x => x.id);
                    table.UniqueConstraint("ux_knowledge_chunks_document_ordinal", x => new { x.knowledge_document_id, x.ordinal });
                    table.CheckConstraint("ck_knowledge_chunks_ordinal", "ordinal >= 0");
                    table.ForeignKey(
                        name: "fk_knowledge_chunks_document",
                        columns: x => new { x.knowledge_document_id, x.tenant_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_index_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_index_attempts", x => x.id);
                    table.CheckConstraint("ck_knowledge_index_attempts_settled", "(status = 'Started') = (completed_at IS NULL)");
                    table.CheckConstraint("ck_knowledge_index_attempts_status", "status IN ('Started', 'Succeeded', 'Failed', 'Unknown')");
                    table.ForeignKey(
                        name: "fk_knowledge_index_attempts_document",
                        columns: x => new { x.document_id, x.tenant_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    knowledge_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    file_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_sources_document",
                        columns: x => new { x.knowledge_document_id, x.tenant_id },
                        principalTable: "knowledge_documents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_knowledge_sources_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    claim_token = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_emails", x => x.id);
                    table.CheckConstraint("ck_notification_emails_attempts", "attempts >= 0");
                    table.ForeignKey(
                        name: "fk_notification_emails_notification",
                        columns: x => new { x.tenant_id, x.notification_id },
                        principalTable: "notifications",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notification_emails_recipient_user",
                        column: x => x.recipient_user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    period_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    schedule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claim_token = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_executions", x => x.id);
                    table.UniqueConstraint("AK_report_executions_tenant_id_id", x => new { x.tenant_id, x.id });
                    table.CheckConstraint("ck_report_executions_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_report_executions_period", "period_to > period_from");
                    table.ForeignKey(
                        name: "fk_report_executions_schedule",
                        columns: x => new { x.tenant_id, x.schedule_id },
                        principalTable: "report_schedules",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_report_executions_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    handling = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversations_assigned_member",
                        columns: x => new { x.assigned_to, x.tenant_id },
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "user_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_customer",
                        columns: x => new { x.tenant_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_tenant",
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

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    tenant_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.tenant_role_id, x.permission });
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

            migrationBuilder.CreateTable(
                name: "web_chat_allowed_origins",
                columns: table => new
                {
                    web_chat_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    allowed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_web_chat_allowed_origins", x => new { x.web_chat_endpoint_id, x.origin });
                    table.ForeignKey(
                        name: "fk_web_chat_allowed_origins_endpoint",
                        columns: x => new { x.web_chat_endpoint_id, x.tenant_id },
                        principalTable: "web_chat_endpoints",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_credentials",
                columns: table => new
                {
                    whatsapp_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_credentials", x => x.whatsapp_connection_id);
                    table.ForeignKey(
                        name: "fk_whatsapp_credentials_connection",
                        column: x => x.whatsapp_connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_inbound_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    whatsapp_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_inbound_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_whatsapp_inbound_connection",
                        column: x => x.whatsapp_connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_artifacts", x => x.id);
                    table.CheckConstraint("ck_report_artifacts_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_report_artifacts_length", "byte_length > 0");
                    table.ForeignKey(
                        name: "fk_report_artifacts_execution",
                        columns: x => new { x.tenant_id, x.execution_id },
                        principalTable: "report_executions",
                        principalColumns: new[] { "tenant_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "character varying(32000)", maxLength: 32000, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ingestion_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "web_chat_sessions",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    web_chat_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    client_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    embedded_origin = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_web_chat_sessions", x => x.conversation_id);
                    table.CheckConstraint("ck_web_chat_sessions_embedded_origin", "(client_mode = 'Embedded' AND embedded_origin IS NOT NULL) OR (client_mode <> 'Embedded' AND embedded_origin IS NULL)");
                    table.ForeignKey(
                        name: "fk_web_chat_sessions_agent",
                        columns: x => new { x.ai_agent_id, x.tenant_id },
                        principalTable: "ai_agents",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_web_chat_sessions_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_web_chat_sessions_endpoint",
                        columns: x => new { x.web_chat_endpoint_id, x.tenant_id },
                        principalTable: "web_chat_endpoints",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    whatsapp_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_whatsapp_contacts_connection",
                        column: x => x.whatsapp_connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_whatsapp_contacts_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_human_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_human_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_human_replies_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_human_replies_message",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_human_replies_sender_member",
                        columns: x => new { x.sender_user_id, x.tenant_id },
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "user_id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_human_replies_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_turns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_message_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    customer_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reply_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_turns", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_turns_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_turns_customer_message",
                        column: x => x.customer_message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    whatsapp_connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_whatsapp_deliveries_connection",
                        column: x => x.whatsapp_connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_whatsapp_deliveries_conversation",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_whatsapp_deliveries_message",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "plans",
                columns: new[] { "id", "code", "created_at", "display_name", "max_ai_agents", "max_knowledge_documents", "max_members", "max_monthly_provider_operations", "max_web_chat_endpoints", "max_whatsapp_connections" },
                values: new object[,]
                {
                    { new Guid("019995b8-0000-7000-8000-00000000f8ee"), "free", new DateTimeOffset(new DateTime(2026, 9, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Free", 1, 10, 2, 200, 1, 0 },
                    { new Guid("01a12000-0000-7000-8000-000000000001"), "legacy", new DateTimeOffset(new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Legacy (unlimited)", null, null, null, null, null, null },
                    { new Guid("01a12000-0000-7000-8000-000000000002"), "development", new DateTimeOffset(new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Development (unlimited)", null, null, null, null, null, null },
                    { new Guid("01a12000-0000-7000-8000-000000000003"), "starter", new DateTimeOffset(new DateTime(2026, 9, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Starter", 3, 25, 5, 2000, 1, 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_agent_knowledge_documents_ai_agent_id_tenant_id",
                table: "ai_agent_knowledge_documents",
                columns: new[] { "ai_agent_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_agent_knowledge_documents_knowledge_document_id_tenant_id",
                table: "ai_agent_knowledge_documents",
                columns: new[] { "knowledge_document_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_agents_tenant",
                table: "ai_agents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_usages_open",
                table: "ai_provider_usages",
                column: "started_at",
                filter: "status = 'Started'");

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_usages_tenant_started",
                table: "ai_provider_usages",
                columns: new[] { "tenant_id", "started_at" });

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
                name: "ix_human_replies_conversation",
                table: "conversation_human_replies",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ux_human_replies_client_message",
                table: "conversation_human_replies",
                columns: new[] { "conversation_id", "client_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_human_replies_message",
                table: "conversation_human_replies",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_conversation_turns_client_message",
                table: "conversation_turns",
                columns: new[] { "conversation_id", "client_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_conversation_turns_customer_message",
                table: "conversation_turns",
                column: "customer_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_assigned_to",
                table: "conversations",
                column: "assigned_to",
                filter: "assigned_to IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_customer_activity",
                table: "conversations",
                columns: new[] { "tenant_id", "customer_id", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_last_activity",
                table: "conversations",
                columns: new[] { "tenant_id", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_started",
                table: "conversations",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_merged_into_customer_id",
                table: "customers",
                column: "merged_into_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_created",
                table: "customers",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_invitation_roles_invitation_id_tenant_id",
                table: "invitation_roles",
                columns: new[] { "invitation_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_roles_role",
                table: "invitation_roles",
                columns: new[] { "tenant_role_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_embedding",
                table: "knowledge_chunks",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_tenant_document",
                table: "knowledge_chunks",
                columns: new[] { "tenant_id", "knowledge_document_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_documents_tenant",
                table: "knowledge_documents",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_index_attempts_document",
                table: "knowledge_index_attempts",
                columns: new[] { "document_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_index_attempts_started",
                table: "knowledge_index_attempts",
                column: "started_at",
                filter: "status = 'Started'");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_sources_tenant",
                table: "knowledge_sources",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_sources_document",
                table: "knowledge_sources",
                column: "knowledge_document_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_ingestion",
                table: "messages",
                columns: new[] { "conversation_id", "ingestion_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_emails_tenant_id_notification_id",
                table: "notification_emails",
                columns: new[] { "tenant_id", "notification_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_emails_claimable",
                table: "notification_emails",
                columns: new[] { "status", "available_at" },
                filter: "status IN ('Pending', 'Sending')");

            migrationBuilder.CreateIndex(
                name: "ux_notification_emails_notification",
                table: "notification_emails",
                column: "notification_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_occurrences_pending",
                table: "notification_occurrences",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notification_occurrences_key",
                table: "notification_occurrences",
                columns: new[] { "tenant_id", "kind", "occurrence_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_expires",
                table: "notifications",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_created",
                table: "notifications",
                columns: new[] { "tenant_id", "recipient_user_id", "created_at", "id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_unread",
                table: "notifications",
                columns: new[] { "tenant_id", "recipient_user_id" },
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_notifications_recipient_dedup",
                table: "notifications",
                columns: new[] { "tenant_id", "recipient_user_id", "deduplication_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_artifacts_tenant_id_execution_id",
                table: "report_artifacts",
                columns: new[] { "tenant_id", "execution_id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_artifacts_expires",
                table: "report_artifacts",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_report_artifacts_execution",
                table: "report_artifacts",
                column: "execution_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_report_artifacts_storage_key",
                table: "report_artifacts",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_report_executions_tenant_id_schedule_id",
                table: "report_executions",
                columns: new[] { "tenant_id", "schedule_id" });

            migrationBuilder.CreateIndex(
                name: "ix_report_executions_claimable",
                table: "report_executions",
                columns: new[] { "status", "requested_at" },
                filter: "status IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "ix_report_executions_tenant_requested",
                table: "report_executions",
                columns: new[] { "tenant_id", "requested_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_report_executions_client_request",
                table: "report_executions",
                columns: new[] { "tenant_id", "requested_by", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_report_executions_occurrence",
                table: "report_executions",
                columns: new[] { "schedule_id", "period_from" },
                unique: true,
                filter: "schedule_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_due",
                table: "report_schedules",
                column: "next_run_at",
                filter: "enabled");

            migrationBuilder.CreateIndex(
                name: "ix_report_schedules_tenant_name",
                table: "report_schedules",
                columns: new[] { "tenant_id", "name" });

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

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_tenant",
                table: "tenant_memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_role_assignments_role",
                table: "tenant_role_assignments",
                columns: new[] { "tenant_role_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_tenant_roles_owner",
                table: "tenant_roles",
                column: "tenant_id",
                unique: true,
                filter: "kind = 'Owner'");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_roles_tenant_name",
                table: "tenant_roles",
                columns: new[] { "tenant_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_allowed_origins_web_chat_endpoint_id_tenant_id",
                table: "web_chat_allowed_origins",
                columns: new[] { "web_chat_endpoint_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_endpoints_ai_agent_id_tenant_id",
                table: "web_chat_endpoints",
                columns: new[] { "ai_agent_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ux_web_chat_endpoints_public_key",
                table: "web_chat_endpoints",
                column: "public_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_web_chat_endpoints_tenant_agent",
                table: "web_chat_endpoints",
                columns: new[] { "tenant_id", "ai_agent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_sessions_ai_agent_id_tenant_id",
                table: "web_chat_sessions",
                columns: new[] { "ai_agent_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_sessions_web_chat_endpoint_id_tenant_id",
                table: "web_chat_sessions",
                columns: new[] { "web_chat_endpoint_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_web_chat_sessions_expires_at",
                table: "web_chat_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_web_chat_sessions_access_token_hash",
                table: "web_chat_sessions",
                column: "access_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_connections_agent",
                table: "whatsapp_connections",
                column: "ai_agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_connections_tenant",
                table: "whatsapp_connections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_whatsapp_connections_phone_number",
                table: "whatsapp_connections",
                column: "phone_number_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_contacts_connection",
                table: "whatsapp_contacts",
                column: "whatsapp_connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_contacts_conversation",
                table: "whatsapp_contacts",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ux_whatsapp_contacts_current",
                table: "whatsapp_contacts",
                columns: new[] { "whatsapp_connection_id", "customer_id" },
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_customer_identities_customer",
                table: "whatsapp_customer_identities",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_deliveries_connection",
                table: "whatsapp_deliveries",
                column: "whatsapp_connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_deliveries_conversation",
                table: "whatsapp_deliveries",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_deliveries_provider_message",
                table: "whatsapp_deliveries",
                column: "provider_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_deliveries_status_created",
                table: "whatsapp_deliveries",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_deliveries_tenant_created",
                table: "whatsapp_deliveries",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_whatsapp_deliveries_message",
                table: "whatsapp_deliveries",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_inbound_status_received",
                table: "whatsapp_inbound_messages",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ux_whatsapp_inbound_provider_message",
                table: "whatsapp_inbound_messages",
                columns: new[] { "whatsapp_connection_id", "provider_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_workspace_billing_customers_provider_customer",
                table: "workspace_billing_customers",
                columns: new[] { "provider", "provider_customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_plan_assignments_plan_id",
                table: "workspace_plan_assignments",
                column: "plan_id");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_agent_knowledge_documents");

            migrationBuilder.DropTable(
                name: "ai_provider_usages");

            migrationBuilder.DropTable(
                name: "billing_checkout_intents");

            migrationBuilder.DropTable(
                name: "billing_events");

            migrationBuilder.DropTable(
                name: "billing_provider_price_mappings");

            migrationBuilder.DropTable(
                name: "conversation_human_replies");

            migrationBuilder.DropTable(
                name: "conversation_turns");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "invitation_roles");

            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropTable(
                name: "knowledge_index_attempts");

            migrationBuilder.DropTable(
                name: "knowledge_sources");

            migrationBuilder.DropTable(
                name: "notification_emails");

            migrationBuilder.DropTable(
                name: "notification_occurrences");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "report_artifacts");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "tenant_role_assignments");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "web_chat_allowed_origins");

            migrationBuilder.DropTable(
                name: "web_chat_sessions");

            migrationBuilder.DropTable(
                name: "whatsapp_contacts");

            migrationBuilder.DropTable(
                name: "whatsapp_credentials");

            migrationBuilder.DropTable(
                name: "whatsapp_customer_identities");

            migrationBuilder.DropTable(
                name: "whatsapp_deliveries");

            migrationBuilder.DropTable(
                name: "whatsapp_inbound_messages");

            migrationBuilder.DropTable(
                name: "workspace_billing_customers");

            migrationBuilder.DropTable(
                name: "workspace_plan_assignments");

            migrationBuilder.DropTable(
                name: "workspace_subscriptions");

            migrationBuilder.DropTable(
                name: "tenant_invitations");

            migrationBuilder.DropTable(
                name: "knowledge_documents");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "report_executions");

            migrationBuilder.DropTable(
                name: "tenant_roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "web_chat_endpoints");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "whatsapp_connections");

            migrationBuilder.DropTable(
                name: "billing_plan_offers");

            migrationBuilder.DropTable(
                name: "report_schedules");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "ai_agents");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "tenant_memberships");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropSequence(
                name: "message_ingestion_sequence");
        }
    }
}
