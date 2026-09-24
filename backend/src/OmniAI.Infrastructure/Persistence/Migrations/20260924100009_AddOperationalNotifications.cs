using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                        name: "fk_notifications_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                });

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

            // Hand-added, like fk_tenant_memberships_user: the Identity user is not
            // part of the Domain model, so EF cannot scaffold these. A notification,
            // a queued email and a preference row all belong to a real account, and
            // go with it if the account is ever deleted. Membership is deliberately
            // not the parent: a member who leaves keeps their history for the
            // retention period, unreachable through any workspace route.
            migrationBuilder.AddForeignKey(
                name: "fk_notifications_recipient_user",
                table: "notifications",
                column: "recipient_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_notification_emails_recipient_user",
                table: "notification_emails",
                column: "recipient_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_notification_preferences_user",
                table: "notification_preferences",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_emails");

            migrationBuilder.DropTable(
                name: "notification_occurrences");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "notifications");
        }
    }
}
