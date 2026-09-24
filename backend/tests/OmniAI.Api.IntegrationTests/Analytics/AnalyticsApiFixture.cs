using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Api.IntegrationTests.Knowledge;
using OmniAI.Domain.Ai;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Analytics;

/// <summary>
/// A hosted application whose AI provider refuses to be called.
/// </summary>
/// <remarks>
/// <strong>The provider is a tripwire, not a stub.</strong> Reading analytics
/// must cost nothing, and the cheapest way to prove it is a generator that throws
/// the moment anybody reaches for it.
/// </remarks>
// Unsealed in Phase 31 so the reports fixture can inherit its seeding helpers
// and its embedding tripwire rather than copying them.
public class AnalyticsApiFixture : RoleApiFixture
{
    private readonly RecordingEmbeddingProvider _embeddings = new();

    internal RecordingEmbeddingProvider Embeddings => _embeddings;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        services.Replace(
            ServiceDescriptor.Singleton<Application.Knowledge.ITextEmbeddingGenerator>(_embeddings));
    }

    /// <summary>
    /// Writes one web chat conversation with a known number of messages.
    /// </summary>
    /// <remarks>
    /// Written straight through the aggregates into the same database the API
    /// reads, so the numbers a test asserts are rows somebody could count by hand
    /// — not a seam the endpoint was told to believe.
    /// </remarks>
    public async Task SeedAsync(
        Guid tenantId,
        string agentName,
        int customerMessages,
        int aiMessages,
        string text = "Some words.")
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TenantId workspace = TenantId.From(tenantId);

        AiAgent agent = AiAgent.Create(
            workspace,
            AiAgentName.Create(agentName),
            AgentInstructions.Create("Be helpful."),
            now);

        Conversation conversation = Conversation.Start(workspace, CustomerId.New(), ChannelKind.WebChat, now);

        WebChatEndpoint endpoint = WebChatEndpoint.Create(
            workspace,
            agent.Id,
            WebChatPublicKey.Create($"{Guid.NewGuid():N}"[..22]),
            now);

        WebChatSession session = WebChatSession.Open(
            conversation.Id,
            workspace,
            endpoint.Id,
            agent.Id,
            WebChatAccessTokenHash.From(Hash()),
            now,
            now.AddHours(4));

        for (int index = 0; index < customerMessages; index++)
        {
            conversation.AddTextMessage(MessageSender.Customer, text, now);
        }

        for (int index = 0; index < aiMessages; index++)
        {
            conversation.AddTextMessage(MessageSender.AiAgent, "An answer.", now);
        }

        await using OmniAiDbContext context = CreateDbContext();

        context.AiAgents.Add(agent);
        context.AddOwner(conversation);
        context.Conversations.Add(conversation);
        context.WebChatEndpoints.Add(endpoint);
        context.WebChatSessions.Add(session);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Writes one accounted provider call, as the ledger would have left it.
    /// </summary>
    /// <remarks>
    /// <strong>Synthetic rows, written locally through the aggregate.</strong>
    /// Nothing here reaches OpenAI, and nothing needs to: what the API layer has
    /// to prove is the route, the permission and the shape on the wire, and a row
    /// the domain accepted is indistinguishable at that level from one a real
    /// call left behind.
    /// </remarks>
    public async Task SeedUsageAsync(
        Guid tenantId,
        string model = "test-model-x",
        AiOperationType operationType = AiOperationType.TextGeneration,
        AiUsagePurpose purpose = AiUsagePurpose.ConversationGeneration,
        AiOperationStatus status = AiOperationStatus.Succeeded,
        long? inputTokens = 1_000,
        long? outputTokens = 200,
        decimal? inputPerMillion = 2.00m,
        string currency = "USD")
    {
        DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(-5);

        AiProviderUsage operation = AiProviderUsage.Begin(
            TenantId.From(tenantId),
            AiProvider.OpenAi,
            operationType,
            purpose,
            ModelName.Create(model),
            now);

        AiModelRates? rates = inputPerMillion is decimal rate
            ? AiModelRates.Create(
                AiProvider.OpenAi,
                ModelName.Create(model),
                operationType,
                CurrencyCode.Create(currency),
                now.AddYears(-1),
                rate,
                cachedInputPerMillion: null,
                outputPerMillion: 8.00m)
            : null;

        switch (status)
        {
            case AiOperationStatus.Succeeded:
                operation.Succeed(
                    ProviderTokenUsage.Create(
                        inputTokens: inputTokens,
                        outputTokens: outputTokens),
                    null,
                    rates,
                    now.AddSeconds(2));

                break;

            case AiOperationStatus.Failed:
                operation.Fail(now.AddSeconds(2));

                break;

            case AiOperationStatus.Unknown:
                operation.MarkUnknown(now.AddSeconds(2));

                break;

            default:
                // Started: dispatched, never settled.
                break;
        }

        await using OmniAiDbContext context = CreateDbContext();

        context.AiProviderUsages.Add(operation);

        await context.SaveChangesAsync();
    }

    private static byte[] Hash()
    {
        byte[] hash = new byte[WebChatAccessTokenHash.Length];

        Random.Shared.NextBytes(hash);

        return hash;
    }
}
