using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The WhatsApp channel: connections, their protected credentials, the
    /// customer-to-conversation mapping, the inbound inbox and the outbound
    /// delivery record.
    /// </summary>
    /// <remarks>
    /// Three hand-written departures from what the scaffolder produced, recorded
    /// here because each is the kind of thing a reviewer should be able to check
    /// rather than take on trust.
    /// <para>
    /// <strong>1. The scaffolder's <c>xmin</c> column is removed.</strong> Same
    /// as every other table on this platform: it is PostgreSQL's own system
    /// column, it already exists, and creating it is both unnecessary and
    /// rejected by <c>CREATE TABLE</c>.
    /// </para>
    /// <para>
    /// <strong>2. The current-contact index is partial, and written by
    /// hand.</strong> EF cannot express "unique where <c>is_current</c>". Without
    /// the filter the index would say a customer may have one conversation ever,
    /// which would make every closed conversation the end of the relationship.
    /// Without the index at all, two webhooks from one person arriving together
    /// would each start a conversation, and the customer would be answered twice
    /// with no shared history.
    /// </para>
    /// <para>
    /// <strong>3. The credential lives in its own table.</strong> Not a
    /// departure from the scaffolder, but the reason the schema has five tables
    /// rather than four: a ciphertext column on <c>whatsapp_connections</c> would
    /// be read by every list, every status check and every webhook routing
    /// lookup. Here exactly one query touches it.
    /// </para>
    /// </remarks>
    public partial class AddWhatsAppCloudApiChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

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
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    // xmin is deliberately NOT created here. It is PostgreSQL's per-row
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

            // <strong>One current conversation per customer, per connection.</strong>
            // Partial, because the same customer may have had several
            // conversations over time and every earlier one is still a row here.
            // Unique, because two webhooks from the same person can be processed
            // by two workers at the same moment, and without this both would
            // start a conversation: the customer would be answered twice, by two
            // threads that cannot see each other's history.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_whatsapp_contacts_current
                    ON whatsapp_contacts (whatsapp_connection_id, customer_id)
                 WHERE is_current
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The hand-written index needs no line of its own: dropping the table
        /// drops it.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "whatsapp_contacts");

            migrationBuilder.DropTable(
                name: "whatsapp_credentials");

            migrationBuilder.DropTable(
                name: "whatsapp_deliveries");

            migrationBuilder.DropTable(
                name: "whatsapp_inbound_messages");

            migrationBuilder.DropTable(
                name: "whatsapp_connections");
        }
    }
}
