using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Ai;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Domain.WhatsApp;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Analytics;

/// <summary>
/// A workspace with real rows in it.
/// </summary>
/// <remarks>
/// <strong>Everything here is written through the domain aggregates and saved to
/// a real PostgreSQL.</strong> Analytics is a set of aggregate queries, and an
/// aggregate query tested against hand-made in-memory objects proves only that
/// LINQ-to-Objects works. What has to be proved is that the expressions translate
/// and that the answers match rows a person could count by hand.
/// </remarks>
internal sealed class AnalyticsWorld(PostgresTestDatabase database)
{
    private readonly PostgresTestDatabase _database =
        database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>
    /// One web chat endpoint per agent, because the database allows one.
    /// </summary>
    /// <remarks>
    /// A unique index covers (workspace, agent): an agent has one web chat
    /// endpoint, and every conversation on it opens a session against that one.
    /// Building a fresh endpoint per conversation would violate a real constraint
    /// and prove nothing about analytics.
    /// </remarks>
    private readonly Dictionary<AiAgentId, WebChatEndpointId> _endpoints = [];

    public Tenant Tenant { get; private set; } = null!;

    public async Task<AnalyticsWorld> StartAsync()
    {
        Tenant = Tenant.Create(
            TenantName.Create("Acme"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = _database.CreateContext();

        context.Tenants.Add(Tenant);

        await context.SaveChangesAsync();

        return this;
    }

    /// <summary>An agent, with a web chat endpoint ready to open sessions on.</summary>
    public async Task<AiAgent> AgentAsync(string name, DateTimeOffset at)
    {
        AiAgent agent = AiAgent.Create(
            Tenant.Id,
            AiAgentName.Create(name),
            AgentInstructions.Create("Be helpful."),
            at);

        await using OmniAiDbContext context = _database.CreateContext();

        context.AiAgents.Add(agent);

        await context.SaveChangesAsync();

        return agent;
    }

    /// <summary>A web chat conversation, attributed to an agent.</summary>
    public async Task<Conversation> WebChatAsync(AiAgent agent, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(agent);

        Conversation conversation = Conversation.Start(Tenant.Id, CustomerId.New(), ChannelKind.WebChat, startedAt);

        await using OmniAiDbContext context = _database.CreateContext();

        if (!_endpoints.TryGetValue(agent.Id, out WebChatEndpointId endpointId))
        {
            WebChatEndpoint endpoint = WebChatEndpoint.Create(
                Tenant.Id,
                agent.Id,
                WebChatPublicKey.Create(Key()),
                startedAt);

            context.WebChatEndpoints.Add(endpoint);

            endpointId = endpoint.Id;
            _endpoints[agent.Id] = endpointId;
        }

        WebChatSession session = WebChatSession.Open(
            conversation.Id,
            Tenant.Id,
            endpointId,
            agent.Id,
            WebChatAccessTokenHash.From(Hash()),
            startedAt,
            startedAt.AddHours(4));

        context.AddOwner(conversation);
        context.Conversations.Add(conversation);
        context.WebChatSessions.Add(session);

        await context.SaveChangesAsync();

        return conversation;
    }

    /// <summary>A WhatsApp connection for an agent.</summary>
    public async Task<WhatsAppConnection> WhatsAppConnectionAsync(
        AiAgent agent,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(agent);

        WhatsAppConnection connection = WhatsAppConnection.Create(
            Tenant.Id,
            agent.Id,
            WabaId.Create(Digits()),
            PhoneNumberId.Create(Digits()),
            "+15550000000",
            at);

        await using OmniAiDbContext context = _database.CreateContext();

        context.WhatsAppConnections.Add(connection);

        await context.SaveChangesAsync();

        return connection;
    }

    /// <summary>A WhatsApp conversation, attributed through its connection.</summary>
    public async Task<Conversation> WhatsAppAsync(
        WhatsAppConnection connection,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Conversation conversation = Conversation.Start(Tenant.Id, CustomerId.New(), ChannelKind.WhatsApp, startedAt);

        WhatsAppContact contact = WhatsAppContact.Start(
            connection.Id,
            Tenant.Id,
            WhatsAppCustomerId.Create($"1555{Random.Shared.Next(1000000, 9999999)}"),
            conversation.Id,
            startedAt);

        await using OmniAiDbContext context = _database.CreateContext();

        context.AddOwner(conversation);
        context.Conversations.Add(conversation);
        context.WhatsAppContacts.Add(contact);

        await context.SaveChangesAsync();

        return conversation;
    }

    /// <summary>Adds messages to a conversation at the given instants.</summary>
    public async Task SayAsync(
        Conversation conversation,
        params (MessageSender Sender, DateTimeOffset At)[] messages)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(messages);

        await using OmniAiDbContext context = _database.CreateContext();

        Conversation tracked = await context.Conversations
            .Include(item => item.Messages)
            .SingleAsync(item => item.Id == conversation.Id);

        foreach ((MessageSender sender, DateTimeOffset at) in messages)
        {
            if (sender == MessageSender.System)
            {
                tracked.AddSystemMessage("A note.", at);
            }
            else
            {
                tracked.AddTextMessage(sender, "Some words.", at);
            }
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Closes a conversation, or hands it to a person.</summary>
    public async Task SetStateAsync(
        Conversation conversation,
        bool? closed = null,
        bool? human = null,
        DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        DateTimeOffset when = at ?? conversation.StartedAt.AddMinutes(5);

        await using OmniAiDbContext context = _database.CreateContext();

        Conversation tracked = await context.Conversations
            .SingleAsync(item => item.Id == conversation.Id);

        if (human == true)
        {
            tracked.TakeOverByHuman(when);
        }

        if (closed == true)
        {
            tracked.Close(when);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>An AI turn that ended a particular way.</summary>
    public async Task TurnAsync(
        Conversation conversation,
        ConversationTurnStatus status,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        await using OmniAiDbContext context = _database.CreateContext();

        Conversation tracked = await context.Conversations
            .Include(item => item.Messages)
            .SingleAsync(item => item.Id == conversation.Id);

        Message customer = tracked.AddTextMessage(MessageSender.Customer, "A question.", at);

        ConversationTurn turn = ConversationTurn.Begin(
            conversation.Id,
            ClientMessageId.Create($"c{Guid.NewGuid():N}"[..24]),
            customer.Id,
            at);

        if (status == ConversationTurnStatus.Completed)
        {
            Message reply = tracked.AddTextMessage(MessageSender.AiAgent, "An answer.", at);

            turn.Complete(reply.Id, at);
        }
        else if (status == ConversationTurnStatus.Failed)
        {
            turn.Fail(at);
        }

        context.ConversationTurns.Add(turn);

        await context.SaveChangesAsync();
    }

    /// <summary>An outbound WhatsApp message in a particular delivery state.</summary>
    public async Task DeliveryAsync(
        WhatsAppConnection connection,
        Conversation conversation,
        WhatsAppDeliveryStatus status,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(conversation);

        await using OmniAiDbContext context = _database.CreateContext();

        Conversation tracked = await context.Conversations
            .Include(item => item.Messages)
            .SingleAsync(item => item.Id == conversation.Id);

        Message message = tracked.AddTextMessage(MessageSender.AiAgent, "An answer.", at);

        WhatsAppDelivery delivery = WhatsAppDelivery.Queue(
            connection.Id,
            Tenant.Id,
            conversation.Id,
            message.Id,
            WhatsAppCustomerId.Create("15551234567"),
            "An answer.",
            at);

        switch (status)
        {
            case WhatsAppDeliveryStatus.Sent:
                delivery.MarkSent(ProviderMessageId.Create($"wamid{Guid.NewGuid():N}"), at);

                break;

            case WhatsAppDeliveryStatus.Failed:
                delivery.MarkFailed("Refused.", at);

                break;

            case WhatsAppDeliveryStatus.Unknown:
                delivery.MarkUnknown("No answer.", at);

                break;

            default:
                // Pending is what Queue produces, which is the state under test.
                break;
        }

        context.WhatsAppDeliveries.Add(delivery);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A provider operation that has already run its course.
    /// </summary>
    /// <remarks>
    /// Written through the aggregate rather than as a hand-built row, so a test
    /// cannot store a combination the domain would refuse -- a cost without a
    /// currency, say, or tokens on an operation nobody began.
    /// </remarks>
    public async Task<AiProviderUsage> UsageAsync(
        DateTimeOffset startedAt,
        AiOperationType operationType = AiOperationType.TextGeneration,
        AiUsagePurpose purpose = AiUsagePurpose.ConversationGeneration,
        string model = "test-model-x",
        AiOperationStatus status = AiOperationStatus.Succeeded,
        ProviderTokenUsage? reported = null,
        AiModelRates? rates = null,
        string? resolvedModel = null)
    {
        AiProviderUsage operation = AiProviderUsage.Begin(
            Tenant.Id,
            AiProvider.OpenAi,
            operationType,
            purpose,
            ModelName.Create(model),
            startedAt);

        switch (status)
        {
            case AiOperationStatus.Succeeded:
                operation.Succeed(
                    reported ?? ProviderTokenUsage.Unreported,
                    ModelName.CreateOrNull(resolvedModel),
                    rates,
                    startedAt.AddSeconds(2));

                break;

            case AiOperationStatus.Failed:
                operation.Fail(startedAt.AddSeconds(2));

                break;

            case AiOperationStatus.Unknown:
                operation.MarkUnknown(startedAt.AddSeconds(2));

                break;

            default:
                // Started is what Begin produces: a call that never came back.
                break;
        }

        await using OmniAiDbContext context = _database.CreateContext();

        context.AiProviderUsages.Add(operation);

        await context.SaveChangesAsync();

        return operation;
    }

    /// <summary>Rates for a model, in a currency, so a test can price by hand.</summary>
    public static AiModelRates Rates(
        string model = "test-model-x",
        string currency = "USD",
        AiOperationType operationType = AiOperationType.TextGeneration,
        decimal inputPerMillion = 2.00m,
        decimal? outputPerMillion = 8.00m) =>
        AiModelRates.Create(
            AiProvider.OpenAi,
            ModelName.Create(model),
            operationType,
            CurrencyCode.Create(currency),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            inputPerMillion,
            cachedInputPerMillion: null,
            outputPerMillion: outputPerMillion);

    private static string Key() => $"{Guid.NewGuid():N}"[..22];

    /// <summary>A unique run of digits. Meta identifiers are numeric.</summary>
    private static string Digits() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{Random.Shared.NextInt64(100000000000, 999999999999)}");

    private static byte[] Hash()
    {
        byte[] hash = new byte[WebChatAccessTokenHash.Length];

        Random.Shared.NextBytes(hash);

        return hash;
    }
}
