using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Customers;

/// <summary>
/// What Phase 30's migration does to a deployment that already has history.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Exercised against the real previous schema.</strong> The database is
/// migrated up to Phase 25, filled with records shaped the way a live deployment's
/// are, and only then taken through the Phase 30 migration. Testing the backfill
/// against the new schema would prove nothing, because the new schema is what the
/// backfill is for.
/// </para>
/// <para>
/// The claims are: nothing is lost, provable relationships are kept, and nothing
/// is invented. The last is the one that matters — a backfill that guessed would
/// join two strangers' histories permanently, and no later phase could take them
/// apart.
/// </para>
/// <para>
/// Every identifier here is synthetic. No real number and no real name appears.
/// </para>
/// </remarks>
public sealed class CustomerBackfillTests : IAsyncLifetime
{
    /// <summary>The last migration before Phase 30.</summary>
    private const string PreviousMigration = "20260922102508_AddSaasSubscriptionBilling";

    private readonly PostgresTestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Existing_history_is_given_owners_without_inventing_any_identity()
    {
        await MigrateToAsync(PreviousMigration);

        Legacy legacy = await SeedAsync();

        await MigrateToLatestAsync();

        await using NpgsqlConnection connection = await OpenAsync();

        // ---- nothing was lost ------------------------------------------

        Assert.Equal(5L, await ScalarAsync(connection, "SELECT count(*) FROM conversations"));
        Assert.Equal(4L, await ScalarAsync(connection, "SELECT count(*) FROM messages"));

        // ---- every conversation has an owner ----------------------------

        Assert.Equal(
            0L,
            await ScalarAsync(
                connection,
                "SELECT count(*) FROM conversations WHERE customer_id IS NULL"));

        // ---- WhatsApp continuity was kept -------------------------------

        // The same sender's two conversations -- one closed, one open -- are one
        // person, because whatsapp_contacts already said so.
        Assert.Equal(
            1L,
            await ScalarAsync(
                connection,
                $"""
                 SELECT count(DISTINCT customer_id) FROM conversations
                 WHERE id IN ('{legacy.WhatsAppClosed}', '{legacy.WhatsAppOpen}')
                 """));

        // And that person has exactly one identity row, dated from the earlier of
        // the two conversations.
        Assert.Equal(
            1L,
            await ScalarAsync(
                connection,
                $"""
                 SELECT count(*) FROM whatsapp_customer_identities
                 WHERE tenant_id = '{legacy.TenantId}'
                   AND whatsapp_customer_id = '{Legacy.SenderA}'
                 """));

        // ---- a different sender is a different person -------------------

        Assert.Equal(
            0L,
            await ScalarAsync(
                connection,
                $"""
                 SELECT count(*) FROM conversations AS a
                 JOIN conversations AS b ON a.customer_id = b.customer_id
                 WHERE a.id = '{legacy.WhatsAppOpen}' AND b.id = '{legacy.WhatsAppOther}'
                 """));

        // ---- web chat and unmatched rows are isolated, never pooled -----

        // Two web conversations the schema cannot link, and one conversation with
        // no channel mapping at all. Three conversations, three customers: the
        // safe direction, because two records can be merged later and one record
        // holding two people cannot be taken apart.
        Assert.Equal(
            3L,
            await ScalarAsync(
                connection,
                $"""
                 SELECT count(DISTINCT customer_id) FROM conversations
                 WHERE id IN (
                     '{legacy.Web}', '{legacy.WhatsAppOther}', '{legacy.Unmatched}')
                 """));

        // ---- workspace isolation held ------------------------------------

        Assert.Equal(
            0L,
            await ScalarAsync(
                connection,
                """
                SELECT count(*) FROM conversations AS conversation
                JOIN customers AS customer ON customer.id = conversation.customer_id
                WHERE customer.tenant_id <> conversation.tenant_id
                """));

        // ---- and every customer the backfill made is canonical -----------

        Assert.Equal(
            0L,
            await ScalarAsync(
                connection,
                "SELECT count(*) FROM customers WHERE merged_into_customer_id IS NOT NULL"));
    }

    // --------------------------------------------------------------- helpers

    /// <summary>The ids the assertions refer back to.</summary>
    private sealed record Legacy(
        Guid TenantId,
        Guid WhatsAppClosed,
        Guid WhatsAppOpen,
        Guid WhatsAppOther,
        Guid Web,
        Guid Unmatched)
    {
        /// <summary>A synthetic sender, in a range reserved for fiction.</summary>
        public const string SenderA = "447700900123";

        /// <summary>A second synthetic sender.</summary>
        public const string SenderB = "447700900456";
    }

    /// <summary>
    /// Records shaped the way a live Phase 25 deployment's are.
    /// </summary>
    /// <remarks>
    /// Written as SQL rather than through the aggregates, because the aggregates
    /// now require a customer and the whole point is a database that has none.
    /// </remarks>
    private async Task<Legacy> SeedAsync()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid agentId = Guid.CreateVersion7();
        Guid connectionId = Guid.CreateVersion7();

        Legacy legacy = new(
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        await using NpgsqlConnection connection = await OpenAsync();

        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO tenants (id, name, slug, status)
             VALUES ('{tenantId}', 'Acme', 't{Guid.NewGuid():N}'::text, 'Active');

             INSERT INTO ai_agents (
                 id, tenant_id, name, instructions, status, created_at, updated_at)
             VALUES (
                 '{agentId}', '{tenantId}', 'Support', 'Answer briefly.',
                 'Active', now(), now());

             INSERT INTO whatsapp_connections (
                 id, tenant_id, ai_agent_id, waba_id, phone_number_id,
                 display_phone_number, status, has_credential, created_at, updated_at)
             VALUES (
                 '{connectionId}', '{tenantId}', '{agentId}', '111222333444555',
                 '{Random.Shared.NextInt64(100_000_000_000_000L, 999_999_999_999_999L)
                     .ToString(CultureInfo.InvariantCulture)}',
                 '+44 7700 900000', 'Active', true, now(), now());
             """);

        // Five conversations: two from one WhatsApp sender, one from another, one
        // from the website, and one with no channel mapping at all.
        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO conversations (
                 id, tenant_id, channel, status, handling, started_at, last_activity_at)
             VALUES
                 ('{legacy.WhatsAppClosed}', '{tenantId}', 'WhatsApp', 'Closed', 'Ai',
                  now() - interval '30 days', now() - interval '30 days'),
                 ('{legacy.WhatsAppOpen}', '{tenantId}', 'WhatsApp', 'Open', 'Ai',
                  now() - interval '1 day', now() - interval '1 day'),
                 ('{legacy.WhatsAppOther}', '{tenantId}', 'WhatsApp', 'Open', 'Ai',
                  now() - interval '2 days', now() - interval '2 days'),
                 ('{legacy.Web}', '{tenantId}', 'WebChat', 'Open', 'Ai',
                  now() - interval '3 days', now() - interval '3 days'),
                 ('{legacy.Unmatched}', '{tenantId}', 'WebChat', 'Closed', 'Ai',
                  now() - interval '90 days', now() - interval '90 days');
             """);

        // Four messages, so "nothing was lost" is a claim about real rows.
        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO messages (
                 id, conversation_id, sender, content_type, text, sent_at,
                 ingestion_sequence)
             VALUES
                 ('{Guid.CreateVersion7()}', '{legacy.WhatsAppClosed}', 'Customer',
                  'Text', 'Old question', now() - interval '30 days',
                  nextval('message_ingestion_sequence')),
                 ('{Guid.CreateVersion7()}', '{legacy.WhatsAppOpen}', 'Customer',
                  'Text', 'New question', now() - interval '1 day',
                  nextval('message_ingestion_sequence')),
                 ('{Guid.CreateVersion7()}', '{legacy.Web}', 'Customer',
                  'Text', 'Website question', now() - interval '3 days',
                  nextval('message_ingestion_sequence')),
                 ('{Guid.CreateVersion7()}', '{legacy.Unmatched}', 'Customer',
                  'Text', 'Forgotten question', now() - interval '90 days',
                  nextval('message_ingestion_sequence'));
             """);

        // The mappings a live deployment would hold: one sender with a superseded
        // and a current conversation, one other sender, and nothing for the web.
        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO whatsapp_contacts (
                 id, whatsapp_connection_id, tenant_id, customer_id,
                 conversation_id, started_at, is_current)
             VALUES
                 ('{Guid.CreateVersion7()}', '{connectionId}', '{tenantId}',
                  '{Legacy.SenderA}', '{legacy.WhatsAppClosed}',
                  now() - interval '30 days', false),
                 ('{Guid.CreateVersion7()}', '{connectionId}', '{tenantId}',
                  '{Legacy.SenderA}', '{legacy.WhatsAppOpen}',
                  now() - interval '1 day', true),
                 ('{Guid.CreateVersion7()}', '{connectionId}', '{tenantId}',
                  '{Legacy.SenderB}', '{legacy.WhatsAppOther}',
                  now() - interval '2 days', true);
             """);

        return legacy;
    }

    private async Task MigrateToAsync(string migration)
    {
        await using OmniAiDbContext context = _database.CreateContext();

        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    private async Task MigrateToLatestAsync()
    {
        await using OmniAiDbContext context = _database.CreateContext();

        await context.Database.MigrateAsync();
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(_database.ConnectionString);

        await connection.OpenAsync();

        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }
}
