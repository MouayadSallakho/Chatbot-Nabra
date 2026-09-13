using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the embeddable widget: its appearance, the websites it may be embedded
    /// on, and the transport a session was opened through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Every new non-nullable column is backfilled deliberately, because
    /// the scaffolder's default would have been wrong.</strong> It produced
    /// <c>defaultValue: ""</c> for four columns, and an empty string is not a
    /// valid value for any of them: <c>client_mode</c>, <c>widget_theme</c> and
    /// <c>widget_launcher_position</c> are stored as enum names, and
    /// <c>widget_accent_color</c> as a hex colour. Every row that already existed
    /// would have failed to read back — not at deployment, but the first time
    /// somebody opened an endpoint. Each column is added nullable, filled with the
    /// value the row actually represents, then made required.
    /// </para>
    /// <para>
    /// <strong>Existing sessions are Hosted, and that is a fact rather than a
    /// guess.</strong> Every session that exists when this runs was opened through
    /// the hosted page, because that is the only transport there has been. Marking
    /// them so is what keeps them working, and what keeps them refusing the
    /// embedded routes.
    /// </para>
    /// <para>
    /// <strong>The check constraint is the invariant the aggregate already
    /// enforces, written where nothing can bypass it.</strong> An embedded session
    /// that does not know its website could not be revoked with that website; a
    /// hosted session claiming one would be claiming something that never happened.
    /// </para>
    /// <para>
    /// No physical <c>xmin</c> column is added anywhere. It is PostgreSQL's own
    /// system column, mapped as a shadow concurrency token, and a real column of
    /// that name would shadow it and silently disable every precondition built on
    /// it. Schema tests assert its absence.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class AddWebChatWidgetEmbedding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // ---------------------------------------------------- sessions ---

            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_sessions
                  ADD COLUMN client_mode character varying(16)
                """);

            // Everything that exists was opened through the hosted page, because
            // until this migration that was the only way in.
            migrationBuilder.Sql(
                """
                UPDATE web_chat_sessions SET client_mode = 'Hosted'
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_sessions
                  ALTER COLUMN client_mode SET NOT NULL
                """);

            migrationBuilder.AddColumn<string>(
                name: "embedded_origin",
                table: "web_chat_sessions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            // Mode and origin are one fact. Enforced here as well as in the
            // aggregate, because a row that disagreed with itself would be a row
            // the runtime could not reason about however it got there.
            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_sessions
                  ADD CONSTRAINT ck_web_chat_sessions_embedded_origin CHECK (
                    (client_mode = 'Embedded' AND embedded_origin IS NOT NULL)
                    OR (client_mode <> 'Embedded' AND embedded_origin IS NULL))
                """);

            // --------------------------------------------------- endpoints ---

            migrationBuilder.AddColumn<string>(
                name: "widget_greeting",
                table: "web_chat_endpoints",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_endpoints
                  ADD COLUMN widget_accent_color character varying(7),
                  ADD COLUMN widget_launcher_position character varying(16),
                  ADD COLUMN widget_theme character varying(16)
                """);

            // The platform defaults, spelled out. They are literals rather than a
            // read of the application's current configuration: a migration must
            // describe one fixed transformation, and re-running it after a default
            // changed would otherwise produce a third database.
            migrationBuilder.Sql(
                """
                UPDATE web_chat_endpoints
                   SET widget_accent_color = '#2563eb',
                       widget_launcher_position = 'Right',
                       widget_theme = 'System'
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_endpoints
                  ALTER COLUMN widget_accent_color SET NOT NULL,
                  ALTER COLUMN widget_launcher_position SET NOT NULL,
                  ALTER COLUMN widget_theme SET NOT NULL
                """);

            // ---------------------------------------------- allowed origins ---

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
                    // The same website twice on one widget is impossible, which is
                    // what makes revoking it a single deterministic act.
                    table.PrimaryKey("pk_web_chat_allowed_origins", x => new { x.web_chat_endpoint_id, x.origin });

                    // Composite, onto (id, tenant_id). One workspace cannot approve
                    // a website for another workspace's endpoint, because the single
                    // tenant_id on the row would have to satisfy a key that
                    // disagrees.
                    table.ForeignKey(
                        name: "fk_web_chat_allowed_origins_endpoint",
                        columns: x => new { x.web_chat_endpoint_id, x.tenant_id },
                        principalTable: "web_chat_endpoints",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_web_chat_allowed_origins_web_chat_endpoint_id_tenant_id",
                table: "web_chat_allowed_origins",
                columns: new[] { "web_chat_endpoint_id", "tenant_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "web_chat_allowed_origins");

            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_sessions
                  DROP CONSTRAINT IF EXISTS ck_web_chat_sessions_embedded_origin
                """);

            migrationBuilder.DropColumn(
                name: "client_mode",
                table: "web_chat_sessions");

            migrationBuilder.DropColumn(
                name: "embedded_origin",
                table: "web_chat_sessions");

            migrationBuilder.DropColumn(
                name: "widget_accent_color",
                table: "web_chat_endpoints");

            migrationBuilder.DropColumn(
                name: "widget_greeting",
                table: "web_chat_endpoints");

            migrationBuilder.DropColumn(
                name: "widget_launcher_position",
                table: "web_chat_endpoints");

            migrationBuilder.DropColumn(
                name: "widget_theme",
                table: "web_chat_endpoints");
        }
    }
}
