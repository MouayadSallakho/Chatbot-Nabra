using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// One table, one index, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>What is missing is the design.</strong> No pricing table, no daily
    /// rollup, no summary table and nothing resembling billing. Rates are
    /// configuration; totals are a <c>GROUP BY</c> over this table; invoices,
    /// plans and quotas do not exist in this platform.
    /// </para>
    /// <para>
    /// <strong>Every token column is nullable.</strong> A <c>NOT NULL DEFAULT 0</c>
    /// would turn "the provider did not report this" into "the provider reported
    /// none", and a report built on that would understate real spend while looking
    /// perfectly precise.
    /// </para>
    /// <para>
    /// <strong>Money is <c>numeric</c>, never <c>double precision</c>.</strong>
    /// A cost that cannot be represented exactly is a cost nobody can reconcile.
    /// </para>
    /// <para>
    /// <strong>The tenant key restricts rather than cascades.</strong> This is
    /// evidence of money actually paid out; deleting a workspace must not silently
    /// erase it.
    /// </para>
    /// <para>
    /// No <c>xmin</c> column, physical or otherwise. Rows are written once and
    /// settled once, and the aggregate already refuses a second settle — there is
    /// no lost update to guard against, and a concurrency check would only add a
    /// way for the settle to fail.
    /// </para>
    /// </remarks>
    public partial class AddAiProviderUsageAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

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
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_usages_tenant_started",
                table: "ai_provider_usages",
                columns: new[] { "tenant_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "ai_provider_usages");
        }
    }
}
