using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Application.Ai;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// The workspace safety budget, under the load it exists for.
/// </summary>
/// <remarks>
/// <strong>This is the limit that bounds an invoice.</strong> Everything else
/// partitions traffic; this one caps how many external provider attempts a
/// workspace can make at all, however the traffic is spread across networks,
/// endpoints and conversations. The assertion that matters is not the status codes
/// but the provider call count: it must never exceed the configured allowance,
/// whatever the request timing.
/// </remarks>
public sealed class WorkspaceGenerationBudgetApiTests(WorkspaceBudgetApiFixture api)
    : IClassFixture<WorkspaceBudgetApiFixture>, IAsyncLifetime
{
    private const string ChatRoot = "/api/public/webchat";

    /// <summary>The workspace burst allowance this host is configured with.</summary>
    private const int Budget = 4;

    public Task InitializeAsync() => api.Redis.ClearAsync();

    public Task DisposeAsync() => api.Redis.ClearAsync();

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    [Fact]
    public async Task Spreading_traffic_across_conversations_buys_no_extra_generations()
    {
        // The point of an aggregate. Each conversation is well within its own
        // allowance; together they are not.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Aggregate");

        RecordingAiTextGenerator generator = api.ResetGenerator();

        int allowed = 0;
        int refused = 0;

        // Six separate conversations, one message each. Four should be answered.
        for (int visitor = 0; visitor < 6; visitor++)
        {
            HttpClient client = await api.CreateVisitorAsync();
            Guid conversation = await StartAsync(client, key);

            using HttpResponseMessage response = await SendAsync(client, conversation, "one");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                allowed++;
            }
            else
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                refused++;
            }
        }

        Assert.Equal(Budget, allowed);
        Assert.Equal(6 - Budget, refused);
        Assert.Equal(Budget, generator.Calls);
    }

    [Fact]
    public async Task Rotating_networks_buys_no_extra_generations_either()
    {
        // Every per-network limit is bypassed by using more networks. This one is
        // not, which is what makes it the answer to a botnet.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Botnet");

        RecordingAiTextGenerator generator = api.ResetGenerator();

        for (int attacker = 1; attacker <= 7; attacker++)
        {
            HttpClient client = await api.CreateVisitorAsync(
                System.Net.IPAddress.Parse($"198.51.100.{attacker + 100}"));

            Guid conversation = await StartAsync(client, key);

            (await SendAsync(client, conversation, "one")).Dispose();
        }

        Assert.Equal(Budget, generator.Calls);
    }

    [Fact]
    public async Task Concurrent_new_turns_never_exceed_the_remaining_budget()
    {
        // The mandatory concurrency proof. Twelve genuinely new turns launched at
        // once against a workspace with four attempts left: Redis atomicity, not
        // process timing, decides the winners, and the provider must be reached
        // exactly four times.
        //
        // A read-then-increment limiter would let most of these through, and the
        // failure would be invisible in every status code -- only the call count
        // shows it.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Burst");

        RecordingAiTextGenerator generator = api.ResetGenerator();

        // Twelve visitors, each with their own conversation and credential, all
        // arranged before any of them sends anything.
        List<(HttpClient Client, Guid Conversation)> visitors = [];

        for (int index = 0; index < 12; index++)
        {
            HttpClient client = await api.CreateVisitorAsync(
                System.Net.IPAddress.Parse($"198.51.100.{index + 20}"));

            visitors.Add((client, await StartAsync(client, key)));
        }

        HttpResponseMessage[] responses = await Task.WhenAll(
            visitors.Select(visitor => Task.Run(() =>
                SendAsync(visitor.Client, visitor.Conversation, "one"))));

        try
        {
            int allowed = responses.Count(
                response => response.StatusCode == HttpStatusCode.OK);

            int refused = responses.Count(
                response => response.StatusCode == HttpStatusCode.TooManyRequests);

            Assert.Equal(Budget, allowed);
            Assert.Equal(12 - Budget, refused);

            // The one that would matter on an invoice.
            Assert.Equal(Budget, generator.Calls);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task One_workspace_exhausting_its_budget_does_not_refuse_another()
    {
        // Deliberate partitioning. A busy or attacked workspace must never take
        // another business's chat down with it.
        (HttpClient firstOwner, Guid firstTenant, _) = await api.CreateOwnerAsync("First Trading");
        (string firstKey, _) =
            await WebChatApiFixture.PublishChatAsync(firstOwner, firstTenant, "First");

        (HttpClient secondOwner, Guid secondTenant, _) =
            await api.CreateOwnerAsync("Second Trading");
        (string secondKey, _) =
            await WebChatApiFixture.PublishChatAsync(secondOwner, secondTenant, "Second");

        api.ResetGenerator();

        // Drain the first workspace.
        for (int attempt = 0; attempt < Budget + 2; attempt++)
        {
            HttpClient client = await api.CreateVisitorAsync();

            (await SendAsync(client, await StartAsync(client, firstKey), "one")).Dispose();
        }

        HttpClient other = await api.CreateVisitorAsync();

        using HttpResponseMessage allowed = await SendAsync(
            other,
            await StartAsync(other, secondKey),
            "one");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task A_failed_provider_attempt_counts_against_the_budget()
    {
        // Intentional, and documented. The budget is consumed before the call and
        // is not refunded: an attacker able to make attempts fail would otherwise
        // have found an unlimited free lane through the safety budget. The
        // workspace made an external attempt either way.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Failures");

        // A provider that answers with nothing usable, which the runtime reports as
        // a bad gateway. It is a genuine external attempt: the request was made and
        // the answer was not usable.
        api.ResetGenerator().PretendNullResult = true;

        int attempted = 0;

        for (int index = 0; index < Budget + 3; index++)
        {
            HttpClient client = await api.CreateVisitorAsync();
            Guid conversation = await StartAsync(client, key);

            using HttpResponseMessage response = await SendAsync(client, conversation, "one");

            if (response.StatusCode == HttpStatusCode.BadGateway)
            {
                attempted++;
                continue;
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        // Four genuine attempts, all of which failed upstream, and the budget is
        // spent. It is not refilled by their failure.
        Assert.Equal(Budget, attempted);
    }

    // ---- Arrangement -----------------------------------------------------

    private static async Task<Guid> StartAsync(HttpClient visitor, string key)
    {
        using HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        StartedConversation? conversation =
            await started.Content.ReadFromJsonAsync<StartedConversation>();

        Assert.NotNull(conversation);

        return conversation.ConversationId;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient visitor,
        Guid conversation,
        string clientMessageId) =>
        visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversation}/messages"),
            new { clientMessageId, text = clientMessageId });
}

/// <summary>
/// A host with a workspace generation budget of four.
/// </summary>
/// <remarks>
/// Every other limit is set generously, so the workspace budget is the one that
/// fires. All of it is ordinary configuration; nothing in the application is
/// aware of the arrangement.
/// </remarks>
public sealed class WorkspaceBudgetApiFixture : RoleApiFixture
{
    private readonly RecordingAiTextGenerator _generator = new();

    internal RecordingAiTextGenerator ResetGenerator()
    {
        _generator.Reset();

        return _generator;
    }

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PublicChatProtection:GenerationsPerWorkspaceBurst:Limit"] = "4",
            ["PublicChatProtection:GenerationsPerWorkspaceBurst:WindowSeconds"] = "120",
            ["PublicChatProtection:GenerationsPerWorkspaceSustained:Limit"] = "4000",
            ["PublicChatProtection:GenerationsPerWorkspaceSustained:WindowSeconds"] = "180",
            ["PublicChatProtection:GenerationsPerWorkspaceDaily:Limit"] = "40000",
            ["PublicChatProtection:GenerationsPerWorkspaceDaily:WindowSeconds"] = "240",

            // Generous, so nothing else is the limit under test.
            ["PublicChatProtection:MessagesPerConversation:Limit"] = "500",
            ["PublicChatProtection:ConversationStartsPerClient:Limit"] = "500",
            ["PublicChatProtection:ConversationStartsPerEndpoint:Limit"] = "500",
            ["PublicChatProtection:RequestsPerClient:Limit"] = "500",
        };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(_generator));
    }
}
