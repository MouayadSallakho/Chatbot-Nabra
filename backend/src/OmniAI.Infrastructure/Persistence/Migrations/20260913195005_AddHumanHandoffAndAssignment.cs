using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Who is answering each conversation, who it is routed to, and who wrote
    /// each reply a person sent.
    /// </summary>
    /// <remarks>
    /// Three hand-written departures from what the scaffolder produced, each
    /// recorded here because each is the kind of thing a reviewer should be able
    /// to check rather than take on trust.
    /// <para>
    /// <strong>1. <c>handling</c> is backfilled, not defaulted.</strong> The
    /// scaffolder adds a NOT NULL column with <c>defaultValue: ""</c>, which puts
    /// an empty string into every existing row — a value the enum has no member
    /// for. Nothing would fail at deployment; the first read of an old
    /// conversation would fail instead, in whatever screen happened to open one.
    /// So the column is added nullable, backfilled with the value each row
    /// actually represents, and only then made NOT NULL.
    /// </para>
    /// <para>
    /// <strong>2. The assignment foreign key is composite, and written by
    /// hand.</strong> EF cannot express <c>(assigned_to, tenant_id)</c> into
    /// <c>tenant_memberships (user_id, tenant_id)</c> without modelling the
    /// workspace's membership as a navigation on the conversation aggregate. The
    /// single-column alternative — <c>assigned_to</c> into the users table —
    /// would let a row name somebody who does not work at that workspace, leaving
    /// an application check as the only thing between that and a customer's
    /// transcript in an outsider's inbox. This makes it unrepresentable.
    /// </para>
    /// <para>
    /// <strong>3. The scaffolder's index over the tenant foreign key is
    /// dropped.</strong> Nothing reads human replies by workspace alone: they are
    /// read by conversation, or by the unique idempotency key. An index that
    /// serves no query is write cost and disk for nothing.
    /// </para>
    /// </remarks>
    public partial class AddHumanHandoffAndAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // ----------------------------------------------- conversations ---

            migrationBuilder.Sql(
                """
                ALTER TABLE conversations
                  ADD COLUMN handling character varying(16)
                """);

            // Every conversation that exists was answered by the AI, because
            // until this migration there was no other way to answer one.
            migrationBuilder.Sql(
                """
                UPDATE conversations SET handling = 'Ai'
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE conversations
                  ALTER COLUMN handling SET NOT NULL
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "assigned_to",
                table: "conversations",
                type: "uuid",
                nullable: true);

            // The composite key described above. NOT VALID is deliberately not
            // used: there are no rows to validate, because the column was created
            // empty one statement ago.
            migrationBuilder.Sql(
                """
                ALTER TABLE conversations
                  ADD CONSTRAINT fk_conversations_assigned_member
                  FOREIGN KEY (assigned_to, tenant_id)
                  REFERENCES tenant_memberships (user_id, tenant_id)
                  ON DELETE RESTRICT
                """);

            // Finding one person's conversations, which is the only query this
            // column has. Partial, because the overwhelming majority of rows are
            // unassigned and indexing those nulls would be indexing nothing
            // anybody searches for.
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_conversations_assigned_to
                  ON conversations (assigned_to)
                  WHERE assigned_to IS NOT NULL
                """);

            // ----------------------------------------------- human replies ---

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
                        name: "fk_human_replies_tenant",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The sender must belong to the workspace the reply was sent in. The
            // same composite guarantee as the assignment above, and the reason
            // this table carries a tenant its message does not.
            migrationBuilder.Sql(
                """
                ALTER TABLE conversation_human_replies
                  ADD CONSTRAINT fk_human_replies_sender_member
                  FOREIGN KEY (sender_user_id, tenant_id)
                  REFERENCES tenant_memberships (user_id, tenant_id)
                  ON DELETE RESTRICT
                """);

            migrationBuilder.CreateIndex(
                name: "ix_human_replies_conversation",
                table: "conversation_human_replies",
                column: "conversation_id");

            // The idempotency boundary. Two concurrent requests carrying the same
            // client message id both pass any in-process check; this is what
            // settles the race.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "conversation_human_replies");

            migrationBuilder.Sql(
                """
                ALTER TABLE conversations
                  DROP CONSTRAINT IF EXISTS fk_conversations_assigned_member
                """);

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_conversations_assigned_to
                """);

            migrationBuilder.DropColumn(
                name: "assigned_to",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "handling",
                table: "conversations");
        }
    }
}
