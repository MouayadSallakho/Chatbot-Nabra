using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

public sealed class ConversationPersistenceTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Start = new(2026, 3, 15, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_conversation_round_trips_with_every_field_intact()
    {
        Tenant tenant = await NewTenantAsync();
        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.Instagram, Start);
        ConversationId originalId = conversation.Id;

        await AddAsync(conversation);

        await using OmniAiDbContext read = database.CreateContext();
        Conversation reloaded = await read.Conversations.SingleAsync(c => c.Id == originalId);

        Assert.Equal(originalId, reloaded.Id);
        Assert.Equal(tenant.Id, reloaded.TenantId);
        Assert.Equal(ChannelKind.Instagram, reloaded.Channel);
        Assert.Equal(ConversationStatus.Open, reloaded.Status);
        Assert.Equal(Start, reloaded.StartedAt);
        Assert.Equal(Start, reloaded.LastActivityAt);
    }

    [Fact]
    public async Task Tenant_ownership_survives_the_round_trip()
    {
        Tenant owner = await NewTenantAsync();
        Tenant other = await NewTenantAsync();

        Conversation conversation = Conversation.Start(owner.Id, CustomerId.New(), ChannelKind.WebChat, Start);
        await AddAsync(conversation);

        await using OmniAiDbContext read = database.CreateContext();
        Conversation reloaded = await read.Conversations.SingleAsync(c => c.Id == conversation.Id);

        Assert.Equal(owner.Id, reloaded.TenantId);
        Assert.NotEqual(other.Id, reloaded.TenantId);
    }

    [Theory]
    [InlineData(ChannelKind.WebChat)]
    [InlineData(ChannelKind.WhatsApp)]
    [InlineData(ChannelKind.Instagram)]
    [InlineData(ChannelKind.Messenger)]
    public async Task Every_channel_kind_round_trips_and_is_stored_by_name(ChannelKind channel)
    {
        Tenant tenant = await NewTenantAsync();
        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), channel, Start);
        await AddAsync(conversation);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT channel FROM conversations WHERE id = '{conversation.Id.Value}'");

        Assert.Equal(channel.ToString(), stored as string);

        await using OmniAiDbContext read = database.CreateContext();
        Conversation reloaded = await read.Conversations.SingleAsync(c => c.Id == conversation.Id);

        Assert.Equal(channel, reloaded.Channel);
    }

    [Fact]
    public async Task A_closed_conversation_round_trips_as_closed_with_its_activity_instant()
    {
        Tenant tenant = await NewTenantAsync();
        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start);
        conversation.Close(Start.AddHours(2));

        await AddAsync(conversation);

        object? storedStatus = await database.ExecuteScalarAsync(
            $"SELECT status FROM conversations WHERE id = '{conversation.Id.Value}'");

        Assert.Equal("Closed", storedStatus as string);

        await using OmniAiDbContext read = database.CreateContext();
        Conversation reloaded = await read.Conversations.SingleAsync(c => c.Id == conversation.Id);

        Assert.Equal(ConversationStatus.Closed, reloaded.Status);
        Assert.Equal(Start, reloaded.StartedAt);
        Assert.Equal(Start.AddHours(2), reloaded.LastActivityAt);
    }

    [Fact]
    public async Task Timestamps_are_stored_as_timestamptz_and_return_in_utc()
    {
        Tenant tenant = await NewTenantAsync();

        // A non-UTC offset on the way in; the Domain normalises it, and the
        // column stores the instant.
        DateTimeOffset withOffset = new(2026, 3, 15, 12, 30, 0, TimeSpan.FromHours(3));
        Conversation conversation = Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WhatsApp, withOffset);
        await AddAsync(conversation);

        object? columnType = await database.ExecuteScalarAsync(
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_name = 'conversations' AND column_name = 'started_at'");

        Assert.Equal("timestamp with time zone", columnType as string);

        await using OmniAiDbContext read = database.CreateContext();
        Conversation reloaded = await read.Conversations.SingleAsync(c => c.Id == conversation.Id);

        Assert.Equal(TimeSpan.Zero, reloaded.StartedAt.Offset);
        Assert.Equal(withOffset.UtcDateTime, reloaded.StartedAt.UtcDateTime);
    }

    [Fact]
    public async Task A_conversation_referencing_a_nonexistent_tenant_is_rejected()
    {
        // The tenant foreign key is what makes an orphaned conversation
        // impossible, whatever the calling code believes.
        //
        // No owner row is written here on purpose. From Phase 30 a conversation
        // also needs a customer, and that key belongs to the same workspace that
        // does not exist -- so writing one would only move the failure. Either
        // constraint refusing is the property this test is about: a conversation
        // whose workspace is fictional does not reach the table.
        Conversation orphan = Conversation.Start(TenantId.New(), CustomerId.New(), ChannelKind.WebChat, Start);

        await using OmniAiDbContext context = database.CreateContext();
        context.Conversations.Add(orphan);

        DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());

        Assert.Contains(
            "fk_conversations_",
            failure.InnerException?.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_tenant_that_still_owns_conversations_is_refused()
    {
        // Restrict, not Cascade: losing a tenant's whole history to a stray
        // delete is exactly the accident this constraint exists to prevent.
        Tenant tenant = await NewTenantAsync();
        await AddAsync(Conversation.Start(tenant.Id, CustomerId.New(), ChannelKind.WebChat, Start));

        await using OmniAiDbContext context = database.CreateContext();
        Tenant loaded = await context.Tenants.SingleAsync(t => t.Id == tenant.Id);
        context.Tenants.Remove(loaded);

        DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());

        Assert.Contains("fk_conversations_tenant", failure.InnerException?.Message, StringComparison.Ordinal);
    }

    private async Task<Tenant> NewTenantAsync()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        return tenant;
    }

    private async Task AddAsync(Conversation conversation)
    {
        await using OmniAiDbContext context = database.CreateContext();
        context.AddOwner(conversation);
        context.Conversations.Add(conversation);
        await context.SaveChangesAsync();
    }
}
