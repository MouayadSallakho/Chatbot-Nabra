using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Two indexes, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Phase 22 added no table.</strong> Analytics reads the operational
    /// records that already exist; what it needed was a way to reach them by the
    /// predicates it actually uses.
    /// </para>
    /// <para>
    /// <c>conversations (tenant_id, started_at)</c> — every dashboard query begins
    /// by narrowing conversations to one workspace and one window, and a window is
    /// on when a conversation <em>began</em>. The existing index pairs the
    /// workspace with <c>last_activity_at</c>, which answers a different question,
    /// so without this one a dashboard scanned the whole table.
    /// </para>
    /// <para>
    /// <c>whatsapp_deliveries (tenant_id, created_at)</c> — the existing index
    /// leads with <c>status</c>, which is what the dispatcher asks. Until now this
    /// table had no index on its workspace column at all, so counting one
    /// workspace's deliveries read every delivery in the deployment.
    /// </para>
    /// <para>
    /// Deliberately not added: anything on <c>messages</c>,
    /// <c>conversation_turns</c>, <c>web_chat_sessions</c> or
    /// <c>whatsapp_contacts</c>. Each of those is reached by a join whose key is
    /// already the leading column of an existing index, from a conversation set
    /// the two indexes above have already narrowed. An index costs something on
    /// every write, and these would buy nothing today.
    /// </para>
    /// </remarks>
    public partial class AddAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_deliveries_tenant_created",
                table: "whatsapp_deliveries",
                columns: ["tenant_id", "created_at"]);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_started",
                table: "conversations",
                columns: ["tenant_id", "started_at"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "ix_whatsapp_deliveries_tenant_created",
                table: "whatsapp_deliveries");

            migrationBuilder.DropIndex(
                name: "ix_conversations_tenant_started",
                table: "conversations");
        }
    }
}
