using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves that two competing writers cannot silently overwrite each other.
/// </summary>
/// <remarks>
/// The platform will take concurrent webhooks, retries and parallel workers, so a
/// lost update is a realistic failure, not a theoretical one. Detection here is
/// the database's: the UPDATE carries the row's <c>xmin</c> in its WHERE clause,
/// so a stale writer matches no row and EF reports it.
/// </remarks>
public sealed class ConcurrencyTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_stale_tenant_update_is_rejected_rather_than_silently_applied()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create(NewSlug()));
        await SaveAsync(tenant);

        // Two independent units of work load the same row.
        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        Tenant loadedByFirst = await first.Tenants.SingleAsync(t => t.Id == tenant.Id);
        Tenant loadedBySecond = await second.Tenants.SingleAsync(t => t.Id == tenant.Id);

        loadedByFirst.Rename(TenantName.Create("Renamed by first"));
        await first.SaveChangesAsync();

        // The second writer's snapshot is now stale.
        loadedBySecond.Rename(TenantName.Create("Renamed by second"));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        // The first writer's value survived; the stale write did not land.
        await using OmniAiDbContext verification = database.CreateContext();
        Tenant reloaded = await verification.Tenants.SingleAsync(t => t.Id == tenant.Id);

        Assert.Equal("Renamed by first", reloaded.Name.Value);
    }

    [Fact]
    public async Task A_stale_conversation_status_change_is_rejected()
    {
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create(NewSlug()));
        Conversation conversation = Conversation.Start(tenant.Id, ChannelKind.WhatsApp, Start);
        await SaveAsync(tenant, conversation);

        await using OmniAiDbContext first = database.CreateContext();
        await using OmniAiDbContext second = database.CreateContext();

        Conversation loadedByFirst = await first.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Conversation loadedBySecond = await second.Conversations.SingleAsync(c => c.Id == conversation.Id);

        loadedByFirst.Close(Start.AddMinutes(1));
        await first.SaveChangesAsync();

        // The second writer still believes the conversation is open. Note the
        // probe has to be an operation that actually changes state: Reopen on an
        // instance whose in-memory status is already Open is idempotent by
        // design, so it would produce no UPDATE and nothing to conflict with.
        loadedBySecond.Close(Start.AddMinutes(5));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using OmniAiDbContext verification = database.CreateContext();
        Conversation reloaded = await verification.Conversations.SingleAsync(c => c.Id == conversation.Id);

        // The first writer's instant survived; the stale writer's did not land.
        Assert.Equal(Start.AddMinutes(1), reloaded.LastActivityAt);
    }

    [Fact]
    public async Task A_stale_message_append_is_rejected_and_leaves_no_partial_write()
    {
        // The realistic version of this race: one worker closes a conversation
        // while another, holding a stale copy, is still appending to it.
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create(NewSlug()));
        Conversation conversation = Conversation.Start(tenant.Id, ChannelKind.WebChat, Start);
        await SaveAsync(tenant, conversation);

        await using OmniAiDbContext closer = database.CreateContext();
        await using OmniAiDbContext appender = database.CreateContext();

        Conversation loadedByCloser = await closer.Conversations.SingleAsync(c => c.Id == conversation.Id);
        Conversation loadedByAppender = await appender.Conversations.SingleAsync(c => c.Id == conversation.Id);

        loadedByCloser.Close(Start.AddMinutes(1));
        await closer.SaveChangesAsync();

        loadedByAppender.AddTextMessage(MessageSender.Customer, "Arrived late", Start.AddMinutes(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => appender.SaveChangesAsync());

        // Appending a message also advances the conversation row, so the whole
        // SaveChanges is one transaction: the rejected update must take the
        // message insert down with it rather than leaving an orphaned row.
        await using OmniAiDbContext verification = database.CreateContext();
        Conversation? reloaded = await verification.FindConversationWithMessagesAsync(conversation.Id);

        Assert.NotNull(reloaded);
        Assert.Empty(reloaded.Messages);
        Assert.Equal(0L, Convert.ToInt64(
            await database.ExecuteScalarAsync("SELECT count(*) FROM messages"),
            CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Sequential_updates_from_a_fresh_load_succeed()
    {
        // The token must not block ordinary, non-competing updates.
        Tenant tenant = Tenant.Create(TenantName.Create("Acme"), TenantSlug.Create(NewSlug()));
        await SaveAsync(tenant);

        for (int round = 1; round <= 3; round++)
        {
            await using OmniAiDbContext context = database.CreateContext();
            Tenant loaded = await context.Tenants.SingleAsync(t => t.Id == tenant.Id);

            loaded.Rename(TenantName.Create($"Acme {round}"));
            await context.SaveChangesAsync();
        }

        await using OmniAiDbContext verification = database.CreateContext();
        Tenant reloaded = await verification.Tenants.SingleAsync(t => t.Id == tenant.Id);

        Assert.Equal("Acme 3", reloaded.Name.Value);
    }

    private static string NewSlug() => $"t{Guid.NewGuid():N}"[..20];

    private async Task SaveAsync(Tenant tenant, Conversation? conversation = null)
    {
        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        if (conversation is not null)
        {
            context.Conversations.Add(conversation);
        }

        await context.SaveChangesAsync();
    }
}
