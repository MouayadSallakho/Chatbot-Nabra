using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Application.Ai;
using OmniAI.Infrastructure.Time;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// The anonymous session: how it is carried, how many can coexist, and when it
/// ends.
/// </summary>
/// <remarks>
/// <strong>Three Phase 15 limitations are fixed here, and each is asserted at the
/// boundary where it actually failed.</strong> A conversation was created by
/// merely opening the page; a second conversation in one browser took the first
/// one's credential away; and a credential lasted as long as its row existed.
/// </remarks>
public sealed class HostedChatSessionApiTests(HostedChatSessionApiFixture api)
    : IClassFixture<HostedChatSessionApiFixture>, IAsyncLifetime
{
    private const string ChatRoot = "/api/public/webchat";
    private const string SessionCookie = "omniai.webchat";

    public Task InitializeAsync()
    {
        HostedChatSessionApiFixture.Clock.Now = HostedChatSessionApiFixture.Noon;

        return api.Redis.ClearAsync();
    }

    public Task DisposeAsync() => api.Redis.ClearAsync();

    private sealed record StartedConversation(Guid ConversationId, string AgentName);

    private sealed record ChatPage(IReadOnlyList<ChatMessage> Items, long? NextCursor);

    private sealed record ChatMessage(Guid Id, string Sender, string Text, DateTimeOffset SentAt);

    // ---- Passive reads create nothing ------------------------------------

    [Fact]
    public async Task Reading_the_public_face_creates_no_conversation_however_often()
    {
        // The mandatory fix. Every page view, every refresh, every theme change and
        // every crawler used to leave an empty conversation in somebody's
        // workspace; the hosted page now reads this and nothing else on arrival.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Passive");

        HttpClient visitor = await api.CreateVisitorAsync();

        long before = await ConversationCountAsync(tenantId);

        for (int visit = 0; visit < 10; visit++)
        {
            using HttpResponseMessage config = await visitor.GetAsync(
                WebChatApiFixture.Relative($"{ChatRoot}/{key}"));

            Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        }

        Assert.Equal(before, await ConversationCountAsync(tenantId));
    }

    [Fact]
    public async Task Reading_the_public_face_issues_no_credential()
    {
        // If it did, the passive load would be a session in all but name.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "No cookie");

        HttpClient visitor = await api.CreateVisitorAsync();

        using HttpResponseMessage config = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}"));

        Assert.Null(WebChatApiFixture.ReadSetCookie(config, SessionCookie));
    }

    [Fact]
    public async Task A_stale_resume_id_is_answered_not_found_and_creates_nothing()
    {
        // What the hosted page meets when the conversation it remembered has gone.
        // It must be told the conversation is not there, not handed a new one --
        // creating a replacement here is how a reload would silently start a fresh
        // conversation and leave the old one orphaned.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Stale");

        HttpClient visitor = await api.CreateVisitorAsync();

        long before = await ConversationCountAsync(tenantId);

        using HttpResponseMessage response = await visitor.GetAsync(
            WebChatApiFixture.Relative(
                $"{ChatRoot}/conversations/{Guid.CreateVersion7()}/messages"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await ConversationCountAsync(tenantId));
    }

    [Fact]
    public async Task One_send_creates_exactly_one_conversation()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "One");

        HttpClient visitor = await api.CreateVisitorAsync();

        long before = await ConversationCountAsync(tenantId);

        // A few passive reads first, exactly as a page load does.
        (await visitor.GetAsync(WebChatApiFixture.Relative($"{ChatRoot}/{key}"))).Dispose();
        (await visitor.GetAsync(WebChatApiFixture.Relative($"{ChatRoot}/{key}"))).Dispose();

        Guid conversation = await StartAsync(visitor, key);

        using HttpResponseMessage sent = await SendAsync(visitor, conversation, "m1");

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal(before + 1, await ConversationCountAsync(tenantId));
    }

    // ---- The credential is scoped to one conversation --------------------

    [Fact]
    public async Task The_credential_is_scoped_to_its_own_conversations_path()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Scoped");

        HttpClient visitor = await api.CreateVisitorAsync();

        using HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        StartedConversation? conversation =
            await started.Content.ReadFromJsonAsync<StartedConversation>();

        Assert.NotNull(conversation);

        string cookie = WebChatApiFixture.ReadSetCookie(started, SessionCookie)
            ?? throw new InvalidOperationException("No conversation cookie was issued.");

        // Narrower than the group: not sent when the page reads the endpoint's
        // public face, not sent when a conversation is started, and not sent to any
        // other conversation's routes.
        Assert.Contains(
            $"path={ChatRoot}/conversations/{conversation.ConversationId}",
            cookie,
            StringComparison.OrdinalIgnoreCase);

        // Everything Phase 15 guaranteed still holds.
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);

        // And the secret is still nowhere in the body.
        string body = await started.Content.ReadAsStringAsync();
        string secret = WebChatApiFixture.ReadCookie(started, SessionCookie)!;

        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_credential_left_at_the_old_broad_path_is_expired()
    {
        // One release's worth of tidying. A browser that talked to Phase 15 still
        // holds a cookie of this name at the broader path, which would also match
        // every per-conversation path -- so a request would carry two cookies of
        // the same name and the server would read whichever the browser listed
        // first. Expiring it removes the ambiguity rather than reasoning about it.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Upgrade");

        HttpClient visitor = await api.CreateVisitorAsync();

        using HttpResponseMessage started = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        IReadOnlyList<string> cookies =
            WebChatApiFixture.ReadSetCookies(started, SessionCookie);

        // Two headers: the expiry for the legacy path, then the real credential.
        Assert.Equal(2, cookies.Count);

        string expired = Assert.Single(
            cookies,
            cookie => cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        Assert.Contains($"path={ChatRoot}", expired, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conversations", expired, StringComparison.OrdinalIgnoreCase);

        // And the real one still carries a value, which is the trap this ordering
        // exists to avoid: deleting after appending would have stripped it.
        string real = Assert.Single(
            cookies,
            cookie => !cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain($"{SessionCookie}=;", real, StringComparison.Ordinal);
    }

    // ---- Several conversations in one browser ----------------------------

    [Fact]
    public async Task Two_conversations_coexist_in_one_cookie_jar()
    {
        // The Phase 15 limitation, fixed. One browser, one cookie jar, two
        // conversations -- and starting the second must not take the first away.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Two tabs");

        HttpClient visitor = await api.CreateVisitorAsync();

        Guid first = await StartAsync(visitor, key);

        await SendAsync(visitor, first, "first-message");

        Guid second = await StartAsync(visitor, key);

        await SendAsync(visitor, second, "second-message");

        Assert.NotEqual(first, second);

        // Both readable, from the same jar, with no headers of our own.
        ChatPage firstPage = await TranscriptAsync(visitor, first);
        ChatPage secondPage = await TranscriptAsync(visitor, second);

        Assert.Contains(firstPage.Items, message => message.Text == "first-message");
        Assert.DoesNotContain(firstPage.Items, message => message.Text == "second-message");

        Assert.Contains(secondPage.Items, message => message.Text == "second-message");
        Assert.DoesNotContain(secondPage.Items, message => message.Text == "first-message");
    }

    [Fact]
    public async Task The_first_conversation_can_still_be_continued_after_a_second_starts()
    {
        // Reading the older conversation is one thing; writing to it is the one
        // that used to be impossible, because the credential had been overwritten.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Continue");

        HttpClient visitor = await api.CreateVisitorAsync();

        Guid first = await StartAsync(visitor, key);
        Guid second = await StartAsync(visitor, key);

        await SendAsync(visitor, second, "in-the-new-one");

        using HttpResponseMessage backToFirst = await SendAsync(visitor, first, "back-to-the-old");

        Assert.Equal(HttpStatusCode.OK, backToFirst.StatusCode);

        ChatPage page = await TranscriptAsync(visitor, first);

        Assert.Contains(page.Items, message => message.Text == "back-to-the-old");
    }

    [Fact]
    public async Task Five_conversations_coexist_without_any_of_them_becoming_unreachable()
    {
        // A stronger version of the same property, because "two work" could be a
        // coincidence of ordering. Each of five is written to after all five exist.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Five");

        HttpClient visitor = await api.CreateVisitorAsync();

        List<Guid> conversations = [];

        for (int index = 0; index < 5; index++)
        {
            conversations.Add(await StartAsync(visitor, key));
        }

        Assert.Equal(5, conversations.Distinct().Count());

        foreach (Guid conversation in conversations)
        {
            using HttpResponseMessage sent = await SendAsync(
                visitor,
                conversation,
                conversation.ToString());

            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        }
    }

    [Fact]
    public async Task A_credential_is_still_only_good_for_its_own_conversation()
    {
        // Paths separate conversations inside one browser; they are not
        // authorisation and are not treated as one. Presenting one conversation's
        // secret at another's path is still refused by the digest comparison.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Isolated");

        HttpClient visitor = await api.CreateVisitorAsync();

        using HttpResponseMessage startedFirst = await visitor.PostAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/{key}/conversations"),
            content: null);

        StartedConversation? first =
            await startedFirst.Content.ReadFromJsonAsync<StartedConversation>();

        string firstSecret = WebChatApiFixture.ReadCookie(startedFirst, SessionCookie)!;

        Guid second = await StartAsync(visitor, key);

        Assert.NotNull(first);

        // A client that deliberately presents the wrong secret at the right path --
        // which no browser would do, and which is exactly what an attacker who had
        // one credential would try.
        HttpClient forger = api.CreateClient();

        forger.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie}={firstSecret}");

        using HttpResponseMessage refused = await forger.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{second}/messages"));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    // ---- Expiry ----------------------------------------------------------

    [Fact]
    public async Task Before_the_expiry_the_conversation_works()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Fresh");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        // The host's configured lifetime is two hours.
        HostedChatSessionApiFixture.Clock.Advance(TimeSpan.FromMinutes(119));

        using HttpResponseMessage sent = await SendAsync(visitor, conversation, "m1");

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
    }

    [Fact]
    public async Task After_the_expiry_the_visitor_is_refused_and_nothing_is_spent()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Expired");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        await SendAsync(visitor, conversation, "m1");

        RecordingAiTextGenerator generator = api.ResetGenerator();

        HostedChatSessionApiFixture.Clock.Advance(TimeSpan.FromHours(2));

        using HttpResponseMessage refusedSend = await SendAsync(visitor, conversation, "m2");
        using HttpResponseMessage refusedRead = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversation}/messages"));

        // Not-found, exactly as an unknown conversation gets. "This conversation
        // exists but your credential expired" would confirm the id to somebody
        // holding a stale copy.
        Assert.Equal(HttpStatusCode.NotFound, refusedSend.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, refusedRead.StatusCode);

        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task An_expired_public_session_leaves_the_admin_history_intact()
    {
        // The visitor's access ends; the workspace's record does not. Expiry is a
        // credential lifetime, not a retention policy.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Kept");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        using (HttpResponseMessage sent =
            await SendAsync(visitor, conversation, "m1", "what the customer said"))
        {
            Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        }

        HostedChatSessionApiFixture.Clock.Advance(TimeSpan.FromHours(3));

        using HttpResponseMessage admin = await owner.GetAsync(
            WebChatApiFixture.Relative(
                $"/api/tenants/{tenantId}/conversations/{conversation}/messages"));

        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);

        ChatPage? page = await admin.Content.ReadFromJsonAsync<ChatPage>();

        Assert.NotNull(page);
        Assert.Contains(page.Items, message => message.Text == "what the customer said");
    }

    [Fact]
    public async Task An_expired_visitor_can_start_a_fresh_conversation()
    {
        // Expiry must not be a dead end. The visitor's old credential is worthless
        // and their next message starts a new conversation, which is what the
        // hosted page does when its remembered id stops working.
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Again");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid old = await StartAsync(visitor, key);

        HostedChatSessionApiFixture.Clock.Advance(TimeSpan.FromHours(3));

        Guid fresh = await StartAsync(visitor, key);

        Assert.NotEqual(old, fresh);

        using HttpResponseMessage sent = await SendAsync(visitor, fresh, "m1");

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
    }

    [Fact]
    public async Task The_stored_expiry_is_the_configured_lifetime_after_the_start()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        (string key, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Stored");

        HttpClient visitor = await api.CreateVisitorAsync();
        Guid conversation = await StartAsync(visitor, key);

        object? stored = await api.Database.ExecuteScalarAsync(
            "SELECT EXTRACT(EPOCH FROM (expires_at - created_at))::bigint "
            + $"FROM web_chat_sessions WHERE conversation_id = '{conversation}'");

        Assert.Equal(7_200L, stored);
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

    /// <summary>
    /// Sends one message.
    /// </summary>
    /// <param name="text">
    /// What the visitor typed, when it differs from the idempotency key. They are
    /// usually the same in these tests because the key is the easiest thing to
    /// assert a transcript against -- but a client message id is a restricted
    /// alphabet, so any text with a space in it has to be given separately.
    /// </param>
    private static Task<HttpResponseMessage> SendAsync(
        HttpClient visitor,
        Guid conversation,
        string clientMessageId,
        string? text = null) =>
        visitor.PostAsJsonAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversation}/messages"),
            new { clientMessageId, text = text ?? clientMessageId });

    private static async Task<ChatPage> TranscriptAsync(HttpClient visitor, Guid conversation)
    {
        using HttpResponseMessage response = await visitor.GetAsync(
            WebChatApiFixture.Relative($"{ChatRoot}/conversations/{conversation}/messages"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ChatPage? page = await response.Content.ReadFromJsonAsync<ChatPage>();

        Assert.NotNull(page);

        return page;
    }

    private async Task<long> ConversationCountAsync(Guid tenantId) =>
        (long)(await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM conversations WHERE tenant_id = '{tenantId}'")
            ?? 0L);
}

/// <summary>
/// A host with a short public session lifetime and a clock a test can move.
/// </summary>
/// <remarks>
/// <strong>Two hours, and a controllable clock, because the alternative is not a
/// test.</strong> Waiting out a real lifetime is impossible, and shortening it to
/// seconds would make every test in the class race against its own arrangement.
/// The clock is the platform's own <c>TimeProvider</c> seam — the same one the
/// agent time tests use — so the expiry logic under test is production's.
/// <para>
/// It is wrapped in <c>MicrosecondTimeProvider</c> exactly as the composition root
/// wraps the system clock, so stored instants round the way they do in production
/// and the stored-expiry assertion is exact.
/// </para>
/// </remarks>
public sealed class HostedChatSessionApiFixture : RoleApiFixture
{
    /// <summary>The instant every test in this class starts at.</summary>
    public static readonly DateTimeOffset Noon =
        new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The clock the hosted application reads.</summary>
    /// <remarks>
    /// Static because the host is built once per class and the tests need to move
    /// the same clock the running application is reading. Each test resets it in
    /// its own initialisation, so nothing leaks between them.
    /// </remarks>
    public static readonly FixedTimeProvider Clock = new(Noon);

    private readonly RecordingAiTextGenerator _generator = new();

    internal RecordingAiTextGenerator ResetGenerator()
    {
        _generator.Reset();

        return _generator;
    }

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PublicChatProtection:SessionLifetimeSeconds"] = "7200",

            // Generous: this class is about sessions, not about limits.
            ["PublicChatProtection:MessagesPerConversation:Limit"] = "500",
            ["PublicChatProtection:ConversationStartsPerClient:Limit"] = "500",
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

        services.Replace(
            ServiceDescriptor.Singleton<TimeProvider>(new MicrosecondTimeProvider(Clock)));

        services.Replace(ServiceDescriptor.Singleton<IAiTextGenerator>(_generator));
    }
}
