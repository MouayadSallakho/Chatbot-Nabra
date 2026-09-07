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
/// What the public chat does when its protection store cannot be reached.
/// </summary>
/// <remarks>
/// <strong>Fail closed, and prove it rather than assume it.</strong> The platform
/// can still do the work perfectly well; it declines because it cannot tell
/// whether it should. Performing an unbounded number of paid, data-creating
/// operations for anonymous strangers because a counter was unavailable is the one
/// outcome that must not be possible, and the alternative — an in-memory fallback
/// — would engage exactly when the platform is most likely to be under attack.
/// <para>
/// The outage is arranged by pointing the host at a port nothing listens on, which
/// is the honest version of "Redis is away": the real adapter, the real client, a
/// real connection failure. No service is replaced and no failure is injected.
/// </para>
/// </remarks>
public sealed class ProtectionOutageApiTests(UnreachableRedisApiFixture api)
    : IClassFixture<UnreachableRedisApiFixture>
{
    private const string ChatRoot = "/api/public/webchat";

    private sealed record PublicConfig(string AgentName, bool Available);

    private sealed record ChatPage(IReadOnlyList<ChatMessage> Items, long? NextCursor);

    private sealed record ChatMessage(Guid Id, string Sender, string Text, DateTimeOffset SentAt);

    [Fact]
    public async Task Starting_a_conversation_is_refused_and_creates_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Closed");

        HttpClient visitor = await api.CreateVisitorAsync();

        long before = await ConversationCountAsync(tenantId);

        using HttpResponseMessage refused = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);

        Assert.Equal(before, await ConversationCountAsync(tenantId));

        // No credential was issued for a conversation that does not exist.
        Assert.Null(WebChatApiFixture.ReadSetCookie(refused, "omniai.webchat"));
    }

    [Fact]
    public async Task Sending_a_message_is_refused_and_reaches_no_provider()
    {
        // There is no conversation to send into, because starting one is refused
        // too -- so this asserts the send route's own refusal on a conversation id
        // that could exist. The property under test is that the refusal happens
        // before anything is looked up or spent.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Closed");

        RecordingAiTextGenerator generator = api.ResetGenerator();

        HttpClient visitor = await api.CreateVisitorAsync();

        using HttpResponseMessage refused = await visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{Guid.CreateVersion7()}/messages"),
            new { clientMessageId = "m1", text = "Hello" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal(0, generator.Calls);

        // And no message was stored anywhere in this workspace.
        Assert.Equal(0L, await MessageCountAsync(tenantId));
        Assert.NotEqual(string.Empty, key);
    }

    [Fact]
    public async Task The_visitor_is_told_nothing_about_the_dependency()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Closed");

        HttpClient visitor = await api.CreateVisitorAsync();

        using HttpResponseMessage refused = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        string body = await refused.Content.ReadAsStringAsync();

        using JsonDocument problem = JsonDocument.Parse(body);

        // The same wording a switched-off chat gets, deliberately. A visitor must
        // not be able to tell an unreachable counter store from a withdrawn
        // endpoint: the difference is a fact about this deployment's internals.
        Assert.Equal("Chat unavailable", problem.RootElement.GetProperty("title").GetString());

        foreach (string leak in (string[])
        [
            "redis", "counter", "store", "protection", "lua", "connection",
            "127.0.0.1", "port", "OmniAiProtectionRedis", "PUBLIC_CHAT",
        ])
        {
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Public_reads_keep_working_throughout()
    {
        // The documented boundary. Reading a chat endpoint's public face creates
        // nothing and costs no provider call, so it is not gated on the protection
        // store -- a visitor mid-conversation does not lose the page because a
        // counter is unavailable.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Readable");

        HttpClient visitor = await api.CreateVisitorAsync();

        using HttpResponseMessage config = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}"));

        Assert.Equal(HttpStatusCode.OK, config.StatusCode);

        PublicConfig? face = await config.Content.ReadFromJsonAsync<PublicConfig>();

        Assert.NotNull(face);
        Assert.Equal("Readable", face.AgentName);
        Assert.True(face.Available);

        // A transcript read for an unknown conversation is still a not-found rather
        // than an unavailable, which proves the read path never consults the store.
        using HttpResponseMessage transcript = await visitor.GetAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{Guid.CreateVersion7()}/messages"));

        Assert.Equal(HttpStatusCode.NotFound, transcript.StatusCode);
    }

    [Fact]
    public async Task The_whole_admin_surface_is_unaffected()
    {
        // Nothing behind the admin boundary is gated on the anonymous protection
        // policy, so an outage in it must not take a workspace's administration
        // away. This is also the "admin historical access remains available" half
        // of the outage contract.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Admin");

        foreach (string route in (string[])
        [
            "/api/auth/me",
            "/api/me/tenants",
            $"/api/tenants/{tenantId}",
            $"/api/tenants/{tenantId}/ai-agents",
            $"/api/tenants/{tenantId}/website-chat",
            $"/api/tenants/{tenantId}/conversations",
            $"/api/tenants/{tenantId}/roles",
            $"/api/tenants/{tenantId}/members",
        ])
        {
            using HttpResponseMessage response =
                await owner.GetAsync(WebChatApiFixture.Relative(route));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task The_liveness_probe_still_answers()
    {
        // An orchestrator must not conclude the process is dead because an optional
        // dependency is away. The probe is deliberately dependency-free.
        HttpClient client = api.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Signing_in_still_works()
    {
        // The public chat protection policy is written for anonymous strangers.
        // Nothing about authentication is behind it.
        (HttpClient owner, _, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage me = await owner.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    private async Task<long> ConversationCountAsync(Guid tenantId) =>
        (long)(await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM conversations WHERE tenant_id = '{tenantId}'")
            ?? 0L);

    private async Task<long> MessageCountAsync(Guid tenantId) =>
        (long)(await api.Database.ExecuteScalarAsync(
            "SELECT count(*) FROM messages m JOIN conversations c ON c.id = m.conversation_id "
            + $"WHERE c.tenant_id = '{tenantId}'")
            ?? 0L);
}

/// <summary>
/// A host whose protection store is configured but unreachable.
/// </summary>
/// <remarks>
/// <strong>A real outage, not an injected failure.</strong> The connection string
/// names port 1 on the loopback interface, where nothing listens and nothing is
/// ever configured to. So the real Redis adapter runs, the real client tries, and
/// the real connection failure is what the platform has to survive — which is
/// exactly what a stopped Redis container looks like from inside the process.
/// <para>
/// Short timeouts so the suite does not wait: an outage that takes five seconds to
/// notice is the same outage, and this keeps the tests quick.
/// </para>
/// </remarks>
public sealed class UnreachableRedisApiFixture : RoleApiFixture
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
            ["ConnectionStrings:OmniAiProtectionRedis"] =
                "127.0.0.1:1,connectTimeout=250,syncTimeout=250,connectRetry=1",
        };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        // Installed so "no provider call happened" is an assertion rather than a
        // consequence of there being no provider at all.
        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(_generator));
    }
}
