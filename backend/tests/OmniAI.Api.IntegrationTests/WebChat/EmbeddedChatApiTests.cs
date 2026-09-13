using System.Net;
using System.Net.Http.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// The embedded widget's own routes, over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No cookie is involved anywhere in this file, and that is the
/// point.</strong> A widget on somebody else's website is a third-party context,
/// where cookies are blocked by default in every current browser. The credential
/// travels in a header the iframe sets explicitly, so the widget behaves
/// identically whether or not third-party cookies exist.
/// </para>
/// <para>
/// <strong>The custom header is also the CSRF defence.</strong> It is not on the
/// CORS safelist, so a browser must preflight it — and this API answers no
/// preflight from any website. Combined with a credential that is not ambient,
/// there is nothing for a forged cross-site request to ride on.
/// </para>
/// </remarks>
public sealed class EmbeddedChatApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string OriginHeader = "X-OmniAI-Embed-Origin";
    private const string SessionHeader = "X-OmniAI-Embed-Session";
    private const string Website = "https://www.example.com";
    private const string OtherWebsite = "https://other.example.com";

    private static Uri Embed(string path) =>
        WebChatApiFixture.Relative($"/api/public/embed{path}");

    private sealed record ConfigPayload(
        string AgentName,
        bool IsAvailable,
        string? Greeting,
        string AccentColor,
        string LauncherPosition,
        string Theme);

    private sealed record StartedPayload(Guid ConversationId, string AgentName, string Credential);

    private sealed record MessagePayload(Guid Id, string Sender, string Text);

    private sealed record TurnPayload(MessagePayload CustomerMessage, MessagePayload Reply);

    private sealed record PagePayload(IReadOnlyList<MessagePayload> Items, long? NextCursor);

    private sealed record ConversationListPayload(IReadOnlyList<object> Items);

    /// <summary>Publishes a chat endpoint and approves one website for its widget.</summary>
    private async Task<(HttpClient Owner, Guid TenantId, string PublicKey)> PublishWidgetAsync(
        string agentName,
        params string[] origins)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, Guid endpointId) =
            await WebChatApiFixture.PublishChatAsync(owner, tenantId, agentName);

        await ApproveAsync(owner, tenantId, endpointId, origins);

        return (owner, tenantId, publicKey);
    }

    /// <summary>Replaces the approved websites, conditionally as the API requires.</summary>
    private static async Task ApproveAsync(
        HttpClient owner,
        Guid tenantId,
        Guid endpointId,
        params string[] origins)
    {
        Uri widget = WebChatApiFixture.Relative(
            $"/api/tenants/{tenantId}/website-chat/{endpointId}/widget");

        HttpResponseMessage read = await owner.GetAsync(widget);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        HttpRequestMessage write = new(HttpMethod.Put, widget)
        {
            Content = JsonContent.Create(
                new
                {
                    greeting = (string?)null,
                    accentColor = "#2563eb",
                    launcherPosition = "Right",
                    theme = "System",
                    allowedOrigins = origins,
                }),
        };

        write.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag?.ToString());

        Assert.Equal(HttpStatusCode.OK, (await owner.SendAsync(write)).StatusCode);
    }

    /// <summary>A request from the widget, carrying the headers the widget carries.</summary>
    private static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? origin,
        string? credential = null,
        object? body = null)
    {
        HttpRequestMessage request = new(method, Embed(path));

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation(OriginHeader, origin);
        }

        if (credential is not null)
        {
            request.Headers.TryAddWithoutValidation(SessionHeader, credential);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<StartedPayload> StartAsync(
        HttpClient visitor,
        string publicKey,
        string origin)
    {
        HttpResponseMessage started = await visitor.SendAsync(
            Request(HttpMethod.Post, $"/{publicKey}/conversations", origin, body: new { }));

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        return await started.Content.ReadFromJsonAsync<StartedPayload>()
            ?? throw new InvalidOperationException("No conversation was returned.");
    }

    // ---- Who may run the widget ------------------------------------------

    [Fact]
    public async Task An_approved_website_is_told_what_to_render()
    {
        (_, _, string publicKey) = await PublishWidgetAsync("Embed ok", Website);

        HttpClient visitor = fixture.CreateClient();

        HttpResponseMessage response = await visitor.SendAsync(
            Request(HttpMethod.Get, $"/{publicKey}", Website));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ConfigPayload? config = await response.Content.ReadFromJsonAsync<ConfigPayload>();

        Assert.NotNull(config);
        Assert.True(config.IsAvailable);
        Assert.Equal("Embed ok", config.AgentName);
        Assert.Equal("#2563eb", config.AccentColor);

        // Never cached: what a widget may do can change at any moment.
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OtherWebsite)]
    [InlineData("https://evil.www.example.com")]
    [InlineData("http://www.example.com")]
    [InlineData("null")]
    [InlineData("not-an-origin")]
    public async Task Anything_but_an_approved_website_gets_the_same_not_found(string? origin)
    {
        // One answer for every reason. Telling them apart would let somebody
        // holding a public key — which is in the customer's own HTML — discover
        // which websites a workspace has authorised.
        (_, _, string publicKey) = await PublishWidgetAsync("Embed refused", Website);

        HttpClient visitor = fixture.CreateClient();

        HttpResponseMessage response = await visitor.SendAsync(
            Request(HttpMethod.Get, $"/{publicKey}", origin));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_key_is_indistinguishable_from_an_unapproved_website()
    {
        HttpClient visitor = fixture.CreateClient();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await visitor.SendAsync(
                Request(HttpMethod.Get, $"/{new string('z', 22)}", Website))).StatusCode);
    }

    [Fact]
    public async Task Removing_a_website_stops_the_widget_at_once()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, Guid endpointId) =
            await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Embed revoked");

        await ApproveAsync(owner, tenantId, endpointId, Website);

        HttpClient visitor = fixture.CreateClient();

        StartedPayload started = await StartAsync(visitor, publicKey, Website);

        await ApproveAsync(owner, tenantId, endpointId, OtherWebsite);

        // The session is still valid and has not expired. What changed is the
        // allowlist, and it is re-read on every turn rather than trusted from the
        // session that was opened earlier.
        HttpResponseMessage refused = await visitor.SendAsync(
            Request(
                HttpMethod.Get,
                $"/conversations/{started.ConversationId}/messages",
                Website,
                started.Credential));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    // ---- Nothing is created until somebody speaks -------------------------

    [Fact]
    public async Task Loading_a_customers_page_creates_no_conversation()
    {
        (HttpClient owner, Guid tenantId, string publicKey) =
            await PublishWidgetAsync("Embed lazy", Website);

        HttpClient visitor = fixture.CreateClient();

        // A page load, a reload, and a visitor opening the launcher all end here.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await visitor.SendAsync(Request(HttpMethod.Get, $"/{publicKey}", Website)))
                    .StatusCode);
        }

        ConversationListPayload? conversations = await owner
            .GetFromJsonAsync<ConversationListPayload>(
                WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations"));

        Assert.NotNull(conversations);
        Assert.Empty(conversations.Items);
    }

    // ---- The credential ---------------------------------------------------

    [Fact]
    public async Task Starting_returns_the_credential_in_the_body_and_sets_no_cookie()
    {
        (_, _, string publicKey) = await PublishWidgetAsync("Embed credential", Website);

        HttpClient visitor = fixture.CreateClient();

        HttpResponseMessage started = await visitor.SendAsync(
            Request(HttpMethod.Post, $"/{publicKey}/conversations", Website, body: new { }));

        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        // Not a cookie, because a third-party cookie would be blocked by default
        // in every current browser and the widget would silently stop working.
        Assert.False(started.Headers.Contains("Set-Cookie"));

        // The one and only time it crosses the wire, and never cached.
        Assert.Equal("no-store", started.Headers.CacheControl?.ToString());

        StartedPayload? payload = await started.Content.ReadFromJsonAsync<StartedPayload>();

        Assert.NotNull(payload);
        Assert.NotEmpty(payload.Credential);
    }

    [Fact]
    public async Task A_conversation_id_without_the_credential_reads_nothing()
    {
        (_, _, string publicKey) = await PublishWidgetAsync("Embed id only", Website);

        HttpClient visitor = fixture.CreateClient();

        StartedPayload started = await StartAsync(visitor, publicKey, Website);

        foreach (string? credential in new[] { null, "", new string('a', 43) })
        {
            HttpResponseMessage refused = await visitor.SendAsync(
                Request(
                    HttpMethod.Get,
                    $"/conversations/{started.ConversationId}/messages",
                    Website,
                    credential));

            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        }
    }

    // ---- The wall between the two transports ------------------------------

    [Fact]
    public async Task An_embedded_credential_does_not_open_the_hosted_routes()
    {
        (_, _, string publicKey) = await PublishWidgetAsync("Embed to hosted", Website);

        HttpClient visitor = fixture.CreateClient();

        StartedPayload started = await StartAsync(visitor, publicKey, Website);

        // The hosted routes authenticate with a cookie. Presenting the embedded
        // credential as one would make the cookie's protections optional.
        HttpRequestMessage hosted = new(
            HttpMethod.Get,
            WebChatApiFixture.Relative(
                $"/api/public/webchat/conversations/{started.ConversationId}/messages"));

        hosted.Headers.TryAddWithoutValidation(
            "Cookie",
            $"omniai.webchat={started.Credential}");

        Assert.Equal(HttpStatusCode.NotFound, (await visitor.SendAsync(hosted)).StatusCode);
    }

    [Fact]
    public async Task A_hosted_credential_does_not_open_the_embedded_routes()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, Guid endpointId) =
            await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Hosted to embed");

        await ApproveAsync(owner, tenantId, endpointId, Website);

        // A hosted start needs the antiforgery token, because the hosted routes
        // authenticate with a cookie. The embedded routes below deliberately do
        // not: this same client makes them with no token at all.
        HttpClient visitor = await fixture.CreateVisitorAsync();

        HttpResponseMessage hostedStart = await visitor.PostAsync(
            WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}/conversations"),
            content: null);

        Assert.Equal(HttpStatusCode.OK, hostedStart.StatusCode);

        StartedPayload? hosted = await hostedStart.Content.ReadFromJsonAsync<StartedPayload>();
        string? cookie = WebChatApiFixture.ReadCookie(hostedStart, "omniai.webchat");

        Assert.NotNull(hosted);
        Assert.NotNull(cookie);

        // The hosted secret, presented the embedded way.
        HttpResponseMessage refused = await visitor.SendAsync(
            Request(
                HttpMethod.Get,
                $"/conversations/{hosted.ConversationId}/messages",
                Website,
                cookie));

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    // ---- A whole turn, through the ordinary runtime ------------------------

    [Fact]
    public async Task An_embedded_turn_is_stored_and_answered_like_any_other()
    {
        (HttpClient owner, Guid tenantId, string publicKey) =
            await PublishWidgetAsync("Embed turn", Website);

        fixture.ResetGenerator();

        HttpClient visitor = fixture.CreateClient();

        StartedPayload started = await StartAsync(visitor, publicKey, Website);

        HttpResponseMessage sent = await visitor.SendAsync(
            Request(
                HttpMethod.Post,
                $"/conversations/{started.ConversationId}/messages",
                Website,
                started.Credential,
                new { clientMessageId = Guid.NewGuid().ToString(), text = "Where is my order?" }));

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        TurnPayload? turn = await sent.Content.ReadFromJsonAsync<TurnPayload>();

        Assert.NotNull(turn);
        Assert.Equal("Where is my order?", turn.CustomerMessage.Text);
        Assert.Equal("AiAgent", turn.Reply.Sender);

        // The visitor can read their own transcript back.
        HttpResponseMessage read = await visitor.SendAsync(
            Request(
                HttpMethod.Get,
                $"/conversations/{started.ConversationId}/messages",
                Website,
                started.Credential));

        PagePayload? page = await read.Content.ReadFromJsonAsync<PagePayload>();

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);

        // And the workspace sees the conversation, exactly as for a hosted one.
        ConversationListPayload? conversations = await owner
            .GetFromJsonAsync<ConversationListPayload>(
                WebChatApiFixture.Relative($"/api/tenants/{tenantId}/conversations"));

        Assert.NotNull(conversations);
        Assert.Single(conversations.Items);
    }

    [Fact]
    public async Task The_api_grants_no_website_cross_origin_read_access()
    {
        // A widget never calls this API from the customer's origin: it calls from
        // the OmniAI iframe, where it is same-origin. So no response here may
        // carry a cross-origin grant, and "*" least of all.
        (_, _, string publicKey) = await PublishWidgetAsync("Embed cors", Website);

        HttpClient visitor = fixture.CreateClient();

        HttpRequestMessage request = Request(HttpMethod.Get, $"/{publicKey}", Website);

        request.Headers.TryAddWithoutValidation("Origin", Website);

        HttpResponseMessage response = await visitor.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
