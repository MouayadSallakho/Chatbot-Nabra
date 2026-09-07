using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmniAI.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives every anonymous web chat session a stored expiry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Three statements rather than one, because a default would have been
    /// wrong.</strong> The scaffolder produced a single <c>AddColumn</c> with a
    /// sentinel default of <c>0001-01-01</c>, which would have marked every session
    /// that already exists as having expired two thousand years ago — silently
    /// cutting off every visitor mid-conversation the moment this was deployed. It
    /// was replaced by hand: add the column nullable, compute each row's expiry
    /// from its own creation instant, then make it required.
    /// </para>
    /// <para>
    /// <strong>The backfill reads <c>created_at</c>, deliberately not
    /// <c>now()</c>.</strong> A session that started twenty-three hours ago has an
    /// hour left, and that is what this writes. Using the migration's own clock
    /// would have handed every existing session a fresh full day, rewriting old
    /// sessions as though they had all begun at deployment — which is exactly the
    /// retroactive extension the stored expiry exists to make impossible. A row
    /// whose window has already passed correctly ends up with an expiry in the
    /// past and stops being admitted, which is the honest outcome rather than a
    /// convenient one.
    /// </para>
    /// <para>
    /// <strong>The interval is a literal, and it has to be.</strong> Twenty-four
    /// hours is the lifetime this platform shipped in the release that introduced
    /// the setting. A migration must describe one fixed transformation of the
    /// data: reading the deployment's current configuration here would mean the
    /// same migration produced different databases depending on when it happened
    /// to run, and re-running it after a settings change would produce a third.
    /// Later changes to the configured lifetime apply to sessions opened after
    /// them, which is the whole point of storing the value per row.
    /// </para>
    /// <para>
    /// No physical <c>xmin</c> column is added anywhere. That system column is
    /// mapped as a shadow property and must never be declared — a real column of
    /// that name would shadow PostgreSQL's own and silently disable every
    /// concurrency check built on it. A schema test asserts it.
    /// </para>
    /// </remarks>
    /// <inheritdoc />
    public partial class AddWebChatSessionExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_sessions
                  ADD COLUMN expires_at timestamp with time zone
                """);

            // Each row from its own creation instant. Sessions older than the
            // window land in the past and are refused from here on, which is what
            // they would have been had the column always existed.
            migrationBuilder.Sql(
                """
                UPDATE web_chat_sessions
                   SET expires_at = created_at + INTERVAL '24 hours'
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE web_chat_sessions
                  ALTER COLUMN expires_at SET NOT NULL
                """);

            migrationBuilder.CreateIndex(
                name: "ix_web_chat_sessions_expires_at",
                table: "web_chat_sessions",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropIndex(
                name: "ix_web_chat_sessions_expires_at",
                table: "web_chat_sessions");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "web_chat_sessions");
        }
    }
}
