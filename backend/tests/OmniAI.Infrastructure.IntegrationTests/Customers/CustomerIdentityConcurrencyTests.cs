using Microsoft.EntityFrameworkCore;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Customers;

/// <summary>
/// One person, one customer, however the messages race.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The safety is the database's, not the process's.</strong> Two webhooks
/// a second apart arrive as two workers, and this platform expects to run more
/// than one instance — so a lock held in memory would prove nothing. What settles
/// the race is the primary key on (workspace, sender): one insert wins, the other
/// reads what the winner wrote.
/// </para>
/// <para>
/// Every number here is synthetic and nothing contacts a provider.
/// </para>
/// </remarks>
public sealed class CustomerIdentityConcurrencyTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private const string Number = "447700900123";

    [Fact]
    public async Task Two_first_messages_from_one_number_produce_one_customer()
    {
        (TenantId tenantId, WhatsAppRoute route) = await WorkspaceAsync();

        // Two independent units of work, exactly as two workers would be.
        Task<ConversationId> left = ResolveAsync(route);
        Task<ConversationId> right = ResolveAsync(route);

        ConversationId[] conversations = await Task.WhenAll(left, right);

        await using OmniAiDbContext context = database.CreateContext();

        // One identity, therefore one customer. Not "usually one".
        Assert.Equal(
            1,
            await context.WhatsAppCustomerIdentities.CountAsync(i => i.TenantId == tenantId));

        CustomerId customerId = await context.WhatsAppCustomerIdentities
            .Where(i => i.TenantId == tenantId)
            .Select(i => i.CustomerId)
            .SingleAsync();

        Assert.Equal(1, await context.Customers.CountAsync(c => c.TenantId == tenantId));

        // Both attempts landed on it, whichever conversation each of them got.
        foreach (ConversationId conversationId in conversations.Distinct())
        {
            Assert.Equal(
                customerId,
                await context.Conversations
                    .Where(c => c.Id == conversationId)
                    .Select(c => c.CustomerId)
                    .SingleAsync());
        }
    }

    [Fact]
    public async Task A_new_conversation_after_the_old_one_closes_keeps_the_same_customer()
    {
        // The behaviour the product is actually about: a customer who comes back
        // next month is the same person, and their history is one history.
        (TenantId tenantId, WhatsAppRoute route) = await WorkspaceAsync();

        ConversationId first = await ResolveAsync(route);

        await CloseAsync(first);

        ConversationId second = await ResolveAsync(route);

        Assert.NotEqual(first, second);

        await using OmniAiDbContext context = database.CreateContext();

        CustomerId[] owners = await context.Conversations
            .Where(c => c.Id == first || c.Id == second)
            .Select(c => c.CustomerId)
            .Distinct()
            .ToArrayAsync();

        Assert.Single(owners);

        Assert.Equal(1, await context.Customers.CountAsync(c => c.TenantId == tenantId));
    }

    [Fact]
    public async Task The_same_number_in_two_workspaces_is_two_people()
    {
        // It has to be: they are two businesses' customer records, and neither may
        // read the other's.
        (TenantId firstTenant, WhatsAppRoute firstRoute) = await WorkspaceAsync();
        (TenantId secondTenant, WhatsAppRoute secondRoute) = await WorkspaceAsync();

        await ResolveAsync(firstRoute);
        await ResolveAsync(secondRoute);

        await using OmniAiDbContext context = database.CreateContext();

        CustomerId first = await context.WhatsAppCustomerIdentities
            .Where(i => i.TenantId == firstTenant)
            .Select(i => i.CustomerId)
            .SingleAsync();

        CustomerId second = await context.WhatsAppCustomerIdentities
            .Where(i => i.TenantId == secondTenant)
            .Select(i => i.CustomerId)
            .SingleAsync();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task A_customer_from_another_workspace_cannot_own_a_conversation()
    {
        // The composite foreign key, not an application check. This is what makes
        // cross-workspace attribution impossible rather than merely unlikely.
        (TenantId owner, _) = await WorkspaceAsync();
        (TenantId stranger, _) = await WorkspaceAsync();

        Customer theirs = Customer.Create(stranger, displayName: null, Noon);

        await using OmniAiDbContext context = database.CreateContext();

        context.Customers.Add(theirs);

        await context.SaveChangesAsync();

        context.Conversations.Add(
            Conversation.Start(owner, theirs.Id, ChannelKind.WebChat, Noon));

        DbUpdateException failure =
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Contains(
            "fk_conversations_customer",
            failure.InnerException?.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_web_chat_conversation_and_its_session_share_one_customer()
    {
        // Web chat has no durable identity across separate chats, so the customer
        // is the conversation's. The session is keyed on the conversation, which
        // is why there is no second column to disagree with this one.
        (TenantId tenantId, _) = await WorkspaceAsync();

        Customer customer = Customer.Create(tenantId, displayName: null, Noon);

        Conversation conversation = Conversation.Start(
            tenantId,
            customer.Id,
            ChannelKind.WebChat,
            Noon);

        await using OmniAiDbContext context = database.CreateContext();

        WebChatEndpoint endpoint = await EndpointAsync(context, tenantId);

        context.Customers.Add(customer);
        context.Conversations.Add(conversation);
        context.WebChatSessions.Add(WebChatSession.Open(
            conversation.Id,
            tenantId,
            endpoint.Id,
            endpoint.AiAgentId,
            WebChatAccessTokenHash.From(new byte[32]),
            Noon,
            Noon.AddHours(1)));

        await context.SaveChangesAsync();

        await using OmniAiDbContext reader = database.CreateContext();

        Assert.Equal(
            customer.Id,
            await reader.Conversations
                .Where(c => c.Id == conversation.Id)
                .Select(c => c.CustomerId)
                .SingleAsync());
    }

    // --------------------------------------------------------------- helpers

    private async Task<ConversationId> ResolveAsync(WhatsAppRoute route)
    {
        await using OmniAiDbContext context = database.CreateContext();

        WhatsAppContactStore store = new(context);

        return await store.ResolveConversationAsync(
            route,
            WhatsAppCustomerId.Create(Number),
            Noon);
    }

    private async Task CloseAsync(ConversationId conversationId)
    {
        await using OmniAiDbContext context = database.CreateContext();

        Conversation conversation = await context.Conversations
            .SingleAsync(c => c.Id == conversationId);

        conversation.Close(Noon.AddHours(1));

        await context.SaveChangesAsync();
    }

    private async Task<(TenantId TenantId, WhatsAppRoute Route)> WorkspaceAsync()
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = database.CreateContext();

        context.Tenants.Add(tenant);

        await context.SaveChangesAsync();

        WebChatEndpoint endpoint = await EndpointAsync(context, tenant.Id);

        WhatsAppConnection connection = WhatsAppConnection.Create(
            tenant.Id,
            endpoint.AiAgentId,
            WabaId.Create("111222333444555"),
            PhoneNumberId.Create(Random.Shared.NextInt64(100_000_000_000_000L, 999_999_999_999_999L)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "+44 7700 900000",
            Noon);

        connection.RecordCredentialStored(Noon);
        connection.Activate(Noon);

        context.WhatsAppConnections.Add(connection);

        await context.SaveChangesAsync();

        return (
            tenant.Id,
            new WhatsAppRoute(connection.Id, tenant.Id, endpoint.AiAgentId, IsServing: true));
    }

    /// <summary>An agent and an endpoint, so a connection and a session have one.</summary>
    private static async Task<WebChatEndpoint> EndpointAsync(
        OmniAiDbContext context,
        TenantId tenantId)
    {
        WebChatEndpoint? existing = await context.WebChatEndpoints
            .FirstOrDefaultAsync(endpoint => endpoint.TenantId == tenantId);

        if (existing is not null)
        {
            return existing;
        }

        Domain.AiAgents.AiAgent agent = Domain.AiAgents.AiAgent.Create(
            tenantId,
            Domain.AiAgents.AiAgentName.Create("Support"),
            Domain.AiAgents.AgentInstructions.Create("Answer briefly."),
            Noon);

        context.AiAgents.Add(agent);

        WebChatEndpoint endpoint = WebChatEndpoint.Create(
            tenantId,
            agent.Id,
            WebChatPublicKey.Create($"{Guid.NewGuid():N}"[..WebChatPublicKey.Length]),
            Noon);

        context.WebChatEndpoints.Add(endpoint);

        await context.SaveChangesAsync();

        return endpoint;
    }
}
