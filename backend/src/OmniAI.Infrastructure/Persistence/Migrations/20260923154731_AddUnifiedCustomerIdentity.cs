using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedCustomerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable to begin with. Every existing conversation needs an owner
            // before the column can be required, and a zero-GUID default would
            // have made every conversation on the deployment belong to one
            // customer that does not exist.
            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                table: "conversations",
                type: "uuid",
                nullable: true);

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


            migrationBuilder.CreateIndex(
                name: "IX_customers_merged_into_customer_id",
                table: "customers",
                column: "merged_into_customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_created",
                table: "customers",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_customer_identities_customer",
                table: "whatsapp_customer_identities",
                columns: new[] { "tenant_id", "customer_id" });

            Backfill(migrationBuilder);

            // Only now that every conversation has an owner.
            migrationBuilder.AlterColumn<Guid>(
                name: "customer_id",
                table: "conversations",
                type: "uuid",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_customer_activity",
                table: "conversations",
                columns: new[] { "tenant_id", "customer_id", "last_activity_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_conversations_customer",
                table: "conversations",
                columns: new[] { "tenant_id", "customer_id" },
                principalTable: "customers",
                principalColumns: new[] { "tenant_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <summary>
        /// Gives every conversation that already exists an owner.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Only relationships the schema can already prove are used, and
        /// nothing is inferred.</strong> No name is compared, no number is matched
        /// for similarity, no message is read. Where the existing data cannot show
        /// that two conversations were the same person, they become two customers.
        /// That is the safe direction: two records for one person can be combined
        /// by an administrator afterwards, and one record wrongly holding two
        /// people&#39;s histories cannot be taken apart at all.
        /// </para>
        /// <para>
        /// <strong>Pass 1, WhatsApp.</strong> <c>whatsapp_contacts</c> already
        /// records which sender each conversation belonged to. Every distinct
        /// (workspace, sender) becomes one customer and one identity row, so a
        /// person&#39;s older closed conversations and their current one land on
        /// the same record - the continuity the runtime keeps from now on. The
        /// customer is dated from their earliest conversation, because that is
        /// when the workspace actually met them.
        /// </para>
        /// <para>
        /// <strong>Pass 2, everything else.</strong> Website chat sessions are
        /// keyed on the conversation itself, so the schema holds no evidence that
        /// two web conversations were the same visitor, and there is none to find.
        /// Each remaining conversation therefore becomes its own customer, dated
        /// from its own start. That covers web chat and any conversation whose
        /// channel mapping is missing, and it is deliberately the same treatment:
        /// an unmatched row is isolated rather than pooled with strangers.
        /// </para>
        /// <para>
        /// Identifiers come from <c>gen_random_uuid()</c>. New customers use UUID
        /// version 7 for index locality; these are version 4, which is right here -
        /// a backfilled row has no meaningful creation ordering to encode, and
        /// inventing one would misstate when the platform learned it.
        /// </para>
        /// </remarks>
        private static void Backfill(MigrationBuilder migrationBuilder)
        {
            // Pass 1: one customer per distinct WhatsApp sender per workspace.
            //
            // The insert and the identity rows are paired by row number within a
            // workspace rather than by RETURNING the sender, because a data
            // modifying CTE cannot return a column it did not write. Both sides
            // are ordered by the same key, so the pairing is exact.
            migrationBuilder.Sql(
                """
                WITH senders AS (
                    SELECT
                        contact.tenant_id,
                        contact.customer_id AS whatsapp_customer_id,
                        MIN(conversation.started_at) AS first_seen_at
                    FROM whatsapp_contacts AS contact
                    JOIN conversations AS conversation
                        ON conversation.id = contact.conversation_id
                    GROUP BY contact.tenant_id, contact.customer_id
                ),
                numbered AS (
                    SELECT
                        tenant_id,
                        whatsapp_customer_id,
                        first_seen_at,
                        ROW_NUMBER() OVER (
                            PARTITION BY tenant_id
                            ORDER BY first_seen_at, whatsapp_customer_id
                        ) AS position
                    FROM senders
                ),
                created AS (
                    INSERT INTO customers (
                        id, tenant_id, display_name, merged_into_customer_id,
                        merged_at, merged_by, created_at, updated_at)
                    SELECT
                        gen_random_uuid(), tenant_id, NULL, NULL,
                        NULL, NULL, first_seen_at, first_seen_at
                    FROM numbered
                    ORDER BY tenant_id, position
                    RETURNING id, tenant_id, created_at
                ),
                assigned AS (
                    SELECT
                        id,
                        tenant_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY tenant_id ORDER BY created_at, id
                        ) AS position
                    FROM created
                )
                INSERT INTO whatsapp_customer_identities (
                    tenant_id, whatsapp_customer_id, customer_id, created_at)
                SELECT
                    numbered.tenant_id,
                    numbered.whatsapp_customer_id,
                    assigned.id,
                    numbered.first_seen_at
                FROM numbered
                JOIN assigned
                    ON assigned.tenant_id = numbered.tenant_id
                    AND assigned.position = numbered.position;
                """);

            migrationBuilder.Sql(
                """
                UPDATE conversations AS conversation
                SET customer_id = identity.customer_id
                FROM whatsapp_contacts AS contact
                JOIN whatsapp_customer_identities AS identity
                    ON identity.tenant_id = contact.tenant_id
                    AND identity.whatsapp_customer_id = contact.customer_id
                WHERE contact.conversation_id = conversation.id
                  AND conversation.customer_id IS NULL;
                """);

            // Pass 2: one customer per remaining conversation.
            migrationBuilder.Sql(
                """
                WITH orphans AS (
                    SELECT id, tenant_id, started_at,
                           gen_random_uuid() AS customer_id
                    FROM conversations
                    WHERE customer_id IS NULL
                ),
                created AS (
                    INSERT INTO customers (
                        id, tenant_id, display_name, merged_into_customer_id,
                        merged_at, merged_by, created_at, updated_at)
                    SELECT customer_id, tenant_id, NULL, NULL,
                           NULL, NULL, started_at, started_at
                    FROM orphans
                    RETURNING id
                )
                UPDATE conversations AS conversation
                SET customer_id = orphans.customer_id
                FROM orphans
                WHERE orphans.id = conversation.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_conversations_customer",
                table: "conversations");

            migrationBuilder.DropTable(
                name: "whatsapp_customer_identities");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropIndex(
                name: "ix_conversations_tenant_customer_activity",
                table: "conversations");

            // The customers this migration invented go with it. They exist only
            // because conversations needed an owner, and nothing else references
            // them once the column and the identity table are gone.
            migrationBuilder.DropColumn(
                name: "customer_id",
                table: "conversations");
        }
    }
}
