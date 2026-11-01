using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "email_token_hash",
                table: "tenant_invitations",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "tenant_invitations",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "account_emails",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "invitation_id",
                table: "account_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "account_emails",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_tenant_invitations_email_token_hash",
                table: "tenant_invitations",
                column: "email_token_hash",
                unique: true,
                filter: "email_token_hash IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_invitations_language",
                table: "tenant_invitations",
                sql: "language IS NULL OR language IN ('en', 'ar')");

            migrationBuilder.CreateIndex(
                name: "IX_account_emails_invitation_id_tenant_id",
                table: "account_emails",
                columns: new[] { "invitation_id", "tenant_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_emails_recipient",
                table: "account_emails",
                sql: "(user_id IS NULL) <> (invitation_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_account_emails_invitation",
                table: "account_emails",
                columns: new[] { "invitation_id", "tenant_id" },
                principalTable: "tenant_invitations",
                principalColumns: new[] { "id", "tenant_id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_emails_invitation",
                table: "account_emails");

            migrationBuilder.DropIndex(
                name: "ux_tenant_invitations_email_token_hash",
                table: "tenant_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_invitations_language",
                table: "tenant_invitations");

            migrationBuilder.DropIndex(
                name: "IX_account_emails_invitation_id_tenant_id",
                table: "account_emails");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_emails_recipient",
                table: "account_emails");

            migrationBuilder.DropColumn(
                name: "email_token_hash",
                table: "tenant_invitations");

            migrationBuilder.DropColumn(
                name: "language",
                table: "tenant_invitations");

            migrationBuilder.DropColumn(
                name: "invitation_id",
                table: "account_emails");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "account_emails");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "account_emails",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
