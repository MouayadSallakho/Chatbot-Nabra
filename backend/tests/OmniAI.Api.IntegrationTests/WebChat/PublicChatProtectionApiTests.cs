using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Application.Ai;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// The rate limit, end to end, through the real HTTP pipeline.
/// </summary>
/// <remarks>
/// <strong>Limits small enough to reach, supplied as ordinary
/// configuration.</strong> Every number these tests rely on arrives the way an
/// operator would supply it — a settings key on the host — so what is exercised
/// is the real limiter, the real Redis, the real handler ordering and the real
/// Problem Details translation. There is no test-only code path, no
/// <c>IsDevelopment</c> branch and no way to switch protection off.
/// </remarks>
public sealed class PublicChatProtectionApiTests(TinyLimitApiFixture api)
    : IClassFixture<TinyLimitApiFixture>, IAsyncLifetime
{
    private const string ChatRoot = "/api/public/webchat";

    /// <summary>Each test starts with this host's counters empty.</summary>
    /// <remarks>
    /// The limits are deliberately tiny, so one test's traffic would otherwise
    /// exhaust the next test's allowance. Scoped to this fixture's own key prefix,
    /// never a flush: other classes are running in parallel.
    /// </remarks>
    public Task InitializeAsync() => api.Redis.ClearAsync();

    public Task DisposeAsync() => api.Redis.ClearAsync();

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    private sealed record ChatTurn(ChatMessage CustomerMessage, ChatMessage Reply);

    private sealed record ChatMessage(Guid Id, string Sender, string Text, DateTimeOffset SentAt);

    private sealed record ChatPage(IReadOnlyList<ChatMessage> Items, long? NextCursor);

    // ---- The 429 contract -------------------------------------------------

    [Fact]
    public async Task Too_many_messages_are_answered_429_with_a_usable_retry_after()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Busy desk");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        api.ResetGenerator();

        // The configured allowance is two messages per conversation.
        for (int message = 1; message <= 2; message++)
        {
            using HttpResponseMessage accepted = await SendAsync(visitor, conversation, $"m{message}");

            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using HttpResponseMessage refused = await SendAsync(visitor, conversation, "m3");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        // A refusal that says "too many" without saying "for how long" invites an
        // immediate retry, which is the behaviour it exists to stop.
        string? retryAfter = refused.Headers.TryGetValues("Retry-After", out var values)
            ? values.FirstOrDefault()
            : null;

        Assert.NotNull(retryAfter);

        int seconds = int.Parse(retryAfter, CultureInfo.InvariantCulture);

        Assert.True(seconds >= 1, "Retry-After must never read as 'retry now'.");
        Assert.True(seconds <= 120, $"Retry-After of {seconds}s exceeded the configured window.");
    }

    [Fact]
    public async Task A_refused_message_reaches_no_provider_and_stores_nothing()
    {
        // The mandatory invariant. A 429 means the provider call count is zero and
        // there is no customer message pretending the turn was accepted.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "No spend");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        RecordingAiTextGenerator generator = api.ResetGenerator();

        await SendAsync(visitor, conversation, "m1");
        await SendAsync(visitor, conversation, "m2");

        int spentBefore = generator.Calls;

        using HttpResponseMessage refused = await SendAsync(visitor, conversation, "m3");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(spentBefore, generator.Calls);

        // Four messages stored: two customer, two replies. The refused turn left
        // nothing at all.
        using HttpResponseMessage transcript = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversation}/messages"));

        ChatPage? page = await transcript.Content.ReadFromJsonAsync<ChatPage>();

        Assert.NotNull(page);
        Assert.Equal(4, page.Items.Count);
        Assert.DoesNotContain(page.Items, message => message.Text == "m3");
    }

    [Fact]
    public async Task The_refusal_reveals_no_quota_and_no_policy()
    {
        // A visitor learns that they should wait and roughly how long. Anything
        // else would be a map: how many requests a minute, from how many networks,
        // before anybody notices.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Quiet");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1");
        await SendAsync(visitor, conversation, "m2");

        using HttpResponseMessage refused = await SendAsync(visitor, conversation, "m3");

        string body = await refused.Content.ReadAsStringAsync();

        using JsonDocument problem = JsonDocument.Parse(body);

        Assert.Equal(
            "Too many requests",
            problem.RootElement.GetProperty("title").GetString());

        foreach (string leak in (string[])
        [
            "redis", "quota", "budget", "counter", "window", "limit of", "lua",
            "workspace", "tenant", "198.51.100", "PublicChatProtection",
        ])
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }

        // Nor the workspace it happened in.
        Assert.DoesNotContain(tenantId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Duplicates do not spend the budget twice ------------------------

    [Fact]
    public async Task Repeating_one_message_does_not_exhaust_the_conversation_allowance()
    {
        // The mandatory end-to-end regression. The allowance is two new messages;
        // sending the same one eight times must spend one of them, because it is
        // one logical turn.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Retries");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        RecordingAiTextGenerator generator = api.ResetGenerator();

        for (int attempt = 0; attempt < 8; attempt++)
        {
            using HttpResponseMessage repeat = await SendAsync(visitor, conversation, "same-id");

            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        }

        Assert.Equal(1, generator.Calls);

        // The second genuinely-new message is still allowed, which it would not be
        // if the repeats had each consumed a generation attempt.
        using HttpResponseMessage second = await SendAsync(visitor, conversation, "another-id");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, generator.Calls);
    }

    // ---- Partitioning ----------------------------------------------------

    [Fact]
    public async Task One_visitor_exhausting_their_conversation_does_not_refuse_another()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Two people");

        HttpClient first = await api.CreateVisitorAsync();
        Guid firstConversation = await StartAsync(first, key);

        await SendAsync(first, firstConversation, "m1");
        await SendAsync(first, firstConversation, "m2");

        using (HttpResponseMessage refused = await SendAsync(first, firstConversation, "m3"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        }

        // A different visitor on a different network, with their own conversation.
        HttpClient second = await api.CreateVisitorAsync(SimulatedPeerStartupFilter.Other);
        Guid secondConversation = await StartAsync(second, key);

        using HttpResponseMessage allowed = await SendAsync(second, secondConversation, "m1");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Starting_conversations_too_often_from_one_network_is_refused()
    {
        // The configured allowance is two starts per network per endpoint.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Starts");

        HttpClient visitor = await api.CreateVisitorAsync();

        for (int start = 1; start <= 2; start++)
        {
            using HttpResponseMessage started = await visitor.PostAsync(
                WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
                content: null);

            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        }

        using HttpResponseMessage refused = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task A_refused_start_creates_no_conversation_row()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "No rows");

        HttpClient visitor = await api.CreateVisitorAsync();

        long before = await ConversationCountAsync(tenantId);

        for (int start = 0; start < 5; start++)
        {
            (await visitor.PostAsync(
                WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
                content: null)).Dispose();
        }

        long after = await ConversationCountAsync(tenantId);

        // Two allowed, three refused, and the refusals left nothing behind.
        Assert.Equal(before + 2, after);
    }

    // ---- Reads and the admin are not gated on the public policy ----------

    [Fact]
    public async Task A_rate_limited_visitor_can_still_read_their_own_transcript()
    {
        // The documented boundary. Reading creates nothing and costs no provider
        // call, so a visitor who has been told to slow down must not also lose the
        // conversation they are in the middle of.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Readable");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1");
        await SendAsync(visitor, conversation, "m2");

        using (HttpResponseMessage refused = await SendAsync(visitor, conversation, "m3"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        }

        using HttpResponseMessage transcript = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversation}/messages"));

        Assert.Equal(HttpStatusCode.OK, transcript.StatusCode);

        using HttpResponseMessage config = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}"));

        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
    }

    [Fact]
    public async Task The_anonymous_policy_is_never_applied_to_the_admin()
    {
        // This phase protects /api/public/webchat and nothing else. A member of a
        // workspace is not an anonymous stranger, and being one must not put them
        // under a policy written for one -- least of all a policy tuned this tightly.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Admin free");

        // Exhaust every public allowance this network has.
        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        for (int attempt = 0; attempt < 6; attempt++)
        {
            (await SendAsync(visitor, conversation, $"m{attempt}")).Dispose();
        }

        // The administrator shares the host and, in this test harness, the network.
        // Far more requests than any public limit would allow, all of them fine.
        foreach (string route in (string[])
        [
            "/api/auth/me",
            $"/api/tenants/{tenantId}",
            $"/api/tenants/{tenantId}/ai-agents",
            $"/api/tenants/{tenantId}/website-chat",
            $"/api/tenants/{tenantId}/conversations",
            $"/api/tenants/{tenantId}/roles",
            $"/api/tenants/{tenantId}/members",
        ])
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                using HttpResponseMessage response =
                    await owner.GetAsync(WebChatApiFixture.Relative(route));

                Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
    }

    [Fact]
    public async Task The_admin_still_sees_the_conversation_a_rate_limit_interrupted()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "History");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1");

        using (HttpResponseMessage refused = await SendAsync(visitor, conversation, "m2"))
        {
            // Second is allowed; third would not be. Either way the admin view is
            // unaffected, which is the point.
            Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        }

        using HttpResponseMessage admin = await owner.GetAsync(
            WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversation}/messages"));

        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);

        ChatPage? page = await admin.Content.ReadFromJsonAsync<ChatPage>();

        Assert.NotNull(page);
        Assert.Equal(4, page.Items.Count);
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

    private async Task<long> ConversationCountAsync(Guid tenantId) =>
        (long)(await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM conversations WHERE tenant_id = '{tenantId}'")
            ?? 0L);
}

/// <summary>
/// A host whose public limits are small enough for a test to reach.
/// </summary>
/// <remarks>
/// <strong>Every limit arrives as configuration, exactly as an operator would set
/// it.</strong> Nothing in the application knows it is being tested: the same
/// options object, the same validation, the same Redis adapter and the same
/// handler ordering serve these tests and production. Making the limits reachable
/// by adding a code path would have tested that code path instead of the product.
/// <para>
/// The generation windows are set generously against a tight per-conversation
/// limit, because most of these tests want the conversation limit to be the one
/// that fires. The suite that needs the workspace budget to fire sets its own.
/// </para>
/// </remarks>
public sealed class TinyLimitApiFixture : RoleApiFixture
{
    private readonly RecordingAiTextGenerator _generator = new();

    internal RecordingAiTextGenerator Generator => _generator;

    /// <summary>Clears recorded provider calls between tests sharing this host.</summary>
    internal RecordingAiTextGenerator ResetGenerator()
    {
        _generator.Reset();

        return _generator;
    }

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Two new messages per conversation, and two starts per network per
            // endpoint. Low enough to reach in a test, and still a real policy
            // rather than a switch.
            ["PublicChatProtection:MessagesPerConversation:Limit"] = "2",
            ["PublicChatProtection:MessagesPerConversation:WindowSeconds"] = "120",
            ["PublicChatProtection:ConversationStartsPerClient:Limit"] = "2",
            ["PublicChatProtection:ConversationStartsPerClient:WindowSeconds"] = "120",

            // Generous, so the conversation limit is the one under test. The
            // workspace budget has a suite of its own.
            ["PublicChatProtection:ConversationStartsPerEndpoint:Limit"] = "500",
            ["PublicChatProtection:RequestsPerClient:Limit"] = "500",
            ["PublicChatProtection:GenerationsPerWorkspaceBurst:Limit"] = "500",
            ["PublicChatProtection:GenerationsPerWorkspaceSustained:Limit"] = "5000",
            ["PublicChatProtection:GenerationsPerWorkspaceDaily:Limit"] = "50000",
        };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        // The provider is a double whose call count is an assertion: "a 429 means
        // nothing was spent" can only be checked by being the provider.
        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(_generator));
    }
}
