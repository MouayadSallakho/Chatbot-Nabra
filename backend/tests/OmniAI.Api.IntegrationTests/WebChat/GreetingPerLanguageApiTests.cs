using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Phase 39.1 (note 9): an English and an Arabic greeting beside today's, which
/// stays the fallback; the public reads choose by the page's <c>lang</c>.
/// </summary>
public sealed class GreetingPerLanguageApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string Website = "https://shop.example.com";
    private const string Fallback = "Hello, how can we help?";
    private const string English = "Hi there, ask us anything.";
    private const string Arabic = "أهلاً بك، كيف نساعدك؟";

    // ---- the admin edit -----------------------------------------------------

    [Fact]
    public async Task The_widget_reads_and_writes_both_greetings_beside_the_fallback()
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, _) = await EndpointAsync();

        JsonElement before = await ReadWidgetAsync(owner, tenantId, endpointId);
        JsonElement empty = before.GetProperty("widget").GetProperty("greetings");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("en").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("ar").ValueKind);

        HttpResponseMessage written = await WriteAsync(owner, tenantId, endpointId, Body(Fallback, new { en = English, ar = Arabic }));
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        Assert.NotNull(written.Headers.ETag);

        JsonElement answered = (await ReadAsync(written)).GetProperty("widget");
        Assert.Equal(Fallback, answered.GetProperty("greeting").GetString());
        Assert.Equal(English, answered.GetProperty("greetings").GetProperty("en").GetString());
        Assert.Equal(Arabic, answered.GetProperty("greetings").GetProperty("ar").GetString());

        JsonElement reread = (await ReadWidgetAsync(owner, tenantId, endpointId)).GetProperty("widget");
        Assert.Equal(English, reread.GetProperty("greetings").GetProperty("en").GetString());
        Assert.Equal(Arabic, reread.GetProperty("greetings").GetProperty("ar").GetString());
    }

    [Fact]
    public async Task Absent_greetings_keep_the_stored_ones_and_an_explicit_null_clears_one()
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, _) = await EndpointAsync();
        await WriteAsync(owner, tenantId, endpointId, Body(Fallback, new { en = English, ar = Arabic }));

        // A client written before Phase 39.1 sends no "greetings": nothing is cleared.
        HttpResponseMessage older = await WriteAsync(owner, tenantId, endpointId, Body("Changed fallback", greetings: null, includeGreetings: false));
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);
        JsonElement kept = (await ReadAsync(older)).GetProperty("widget");
        Assert.Equal("Changed fallback", kept.GetProperty("greeting").GetString());
        Assert.Equal(English, kept.GetProperty("greetings").GetProperty("en").GetString());
        Assert.Equal(Arabic, kept.GetProperty("greetings").GetProperty("ar").GetString());

        // "greetings": null keeps them too.
        HttpResponseMessage nullObject = await WriteAsync(owner, tenantId, endpointId, Body(Fallback, greetings: null));
        Assert.Equal(English, (await ReadAsync(nullObject)).GetProperty("widget").GetProperty("greetings").GetProperty("en").GetString());

        // Present: both are set; null and blank clear one.
        HttpResponseMessage cleared = await WriteAsync(owner, tenantId, endpointId, Body(Fallback, new { en = (string?)null, ar = "   " }));
        JsonElement none = (await ReadAsync(cleared)).GetProperty("widget").GetProperty("greetings");
        Assert.Equal(JsonValueKind.Null, none.GetProperty("en").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("ar").ValueKind);
    }

    [Fact]
    public async Task The_edit_keeps_its_if_match_rule()
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, _) = await EndpointAsync();

        HttpResponseMessage read = await owner.GetAsync(WidgetUri(tenantId, endpointId));
        string stale = read.Headers.ETag!.ToString();

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(owner, tenantId, endpointId, Body(Fallback, new { en = English, ar = Arabic }), stale)).StatusCode);

        HttpResponseMessage refused = await PutAsync(owner, tenantId, endpointId, Body(Fallback, new { en = "Lost", ar = "Lost" }), stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);

        HttpResponseMessage missing = await PutAsync(owner, tenantId, endpointId, Body(Fallback, new { en = "Lost", ar = "Lost" }), ifMatch: null);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        JsonElement greetings = (await ReadWidgetAsync(owner, tenantId, endpointId)).GetProperty("widget").GetProperty("greetings");
        Assert.Equal(English, greetings.GetProperty("en").GetString());
    }

    [Theory]
    [InlineData("greeting")]
    [InlineData("greetings.en")]
    [InlineData("greetings.ar")]
    public async Task A_refusal_names_the_member_with_the_existing_codes(string field)
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, _) = await EndpointAsync();
        string tooLong = new('x', 301);

        foreach ((string bad, string code) in new[]
        {
            (tooLong, "website-chat.greeting.too_long"),
            ("Hello\u0007", "website-chat.greeting.control_characters"),
        })
        {
            object body = field switch
            {
                "greeting" => Body(bad, new { en = English, ar = Arabic }),
                "greetings.en" => Body(Fallback, new { en = bad, ar = Arabic }),
                _ => Body(Fallback, new { en = English, ar = bad }),
            };

            HttpResponseMessage refused = await WriteAsync(owner, tenantId, endpointId, body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

            JsonElement problem = await ReadAsync(refused);
            Assert.Equal(code, problem.GetProperty("code").GetString());
            Assert.Equal(field, problem.GetProperty("field").GetString());
        }
    }

    [Fact]
    public async Task Only_website_chat_manage_may_write_them_and_another_workspace_gets_nothing()
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, _) = await EndpointAsync();
        HttpClient viewer = await fixture.CreateRestrictedMemberAsync(tenantId, "website-chat.view");

        HttpResponseMessage read = await viewer.GetAsync(WidgetUri(tenantId, endpointId));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        HttpResponseMessage denied = await PutAsync(viewer, tenantId, endpointId, Body(Fallback, new { en = English, ar = Arabic }), read.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        (HttpClient rival, Guid rivalTenantId, _) = await fixture.CreateOwnerAsync("Rival Greeting Trading");
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(WidgetUri(tenantId, endpointId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync(WidgetUri(rivalTenantId, endpointId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(rival, tenantId, endpointId, Body(Fallback, new { en = "x", ar = "x" }), read.Headers.ETag!.ToString())).StatusCode);

        JsonElement greetings = (await ReadWidgetAsync(owner, tenantId, endpointId)).GetProperty("widget").GetProperty("greetings");
        Assert.Equal(JsonValueKind.Null, greetings.GetProperty("en").ValueKind);
    }

    // ---- the public reads ----------------------------------------------------

    [Theory]
    [InlineData("en", English)]
    [InlineData("EN", English)]
    [InlineData(" en ", English)]
    [InlineData("ar", Arabic)]
    [InlineData("Ar", Arabic)]
    [InlineData(null, Fallback)]
    [InlineData("", Fallback)]
    [InlineData("fr", Fallback)]
    [InlineData("ar-SA", Fallback)]
    [InlineData("english-but-much-too-long", Fallback)]
    public async Task Both_public_reads_give_the_page_language_s_greeting_else_the_fallback(string? lang, string expected)
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, string publicKey) = await EndpointAsync();
        await WriteAsync(owner, tenantId, endpointId, Body(Fallback, new { en = English, ar = Arabic }));

        HttpClient visitor = fixture.CreateClient();
        string query = lang is null ? string.Empty : $"?lang={Uri.EscapeDataString(lang)}";

        JsonElement hosted = await ReadAsync(await visitor.GetAsync(WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}{query}")));
        Assert.Equal(expected, hosted.GetProperty("greeting").GetString());

        JsonElement embedded = await ReadAsync(await visitor.SendAsync(Embed(publicKey, query)));
        Assert.Equal(expected, embedded.GetProperty("greeting").GetString());
    }

    [Fact]
    public async Task A_language_without_its_own_greeting_gets_the_fallback_and_nothing_set_is_null()
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, string publicKey) = await EndpointAsync();
        HttpClient visitor = fixture.CreateClient();

        await WriteAsync(owner, tenantId, endpointId, Body(Fallback, new { en = (string?)null, ar = Arabic }));
        Assert.Equal(Fallback, (await ReadAsync(await visitor.GetAsync(WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}?lang=en")))).GetProperty("greeting").GetString());
        Assert.Equal(Arabic, (await ReadAsync(await visitor.GetAsync(WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}?lang=ar")))).GetProperty("greeting").GetString());

        await WriteAsync(owner, tenantId, endpointId, Body(greeting: null, new { en = (string?)null, ar = (string?)null }));
        JsonElement none = await ReadAsync(await visitor.GetAsync(WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}?lang=ar")));
        Assert.Equal(JsonValueKind.Null, none.GetProperty("greeting").ValueKind);
    }

    [Fact]
    public async Task The_public_reads_carry_no_new_member_and_keep_their_cache_rule()
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, string publicKey) = await EndpointAsync();
        await WriteAsync(owner, tenantId, endpointId, Body(Fallback, new { en = English, ar = Arabic }));

        HttpClient visitor = fixture.CreateClient();

        foreach (string query in new[] { string.Empty, "?lang=en", "?lang=ar" })
        {
            HttpResponseMessage hosted = await visitor.GetAsync(WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}{query}"));
            Assert.Equal(
                ["accentColor", "agentName", "available", "greeting", "theme", "workspaceName"],
                Names(await ReadAsync(hosted)));

            HttpResponseMessage embedded = await visitor.SendAsync(Embed(publicKey, query));
            Assert.Equal(
                ["accentColor", "agentName", "greeting", "isAvailable", "launcherPosition", "theme", "workspaceName"],
                Names(await ReadAsync(embedded)));
            Assert.True(embedded.Headers.CacheControl?.NoStore, "The embedded read must stay no-store.");

            // Neither public read ever says "greetings": the other language's text
            // stays on the admin side.
            string both = await hosted.Content.ReadAsStringAsync() + await embedded.Content.ReadAsStringAsync();
            Assert.DoesNotContain("greetings", both, StringComparison.Ordinal);
        }

        // Only the chosen greeting is published; the other language's is not.
        string english = await visitor.GetStringAsync(WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}?lang=en"));
        Assert.DoesNotContain(Arabic, english, StringComparison.Ordinal);
        Assert.DoesNotContain(Fallback, english, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_workspace_s_key_is_unaffected_and_an_unknown_key_is_still_the_generic_404()
    {
        (HttpClient owner, Guid tenantId, Guid endpointId, _) = await EndpointAsync();
        await WriteAsync(owner, tenantId, endpointId, Body(Fallback, new { en = English, ar = Arabic }));

        (HttpClient rival, Guid rivalTenantId, Guid rivalEndpoint, string rivalKey) = await EndpointAsync("Rival Greeting Shop");
        await WriteAsync(rival, rivalTenantId, rivalEndpoint, Body("Rival hello", greetings: null, includeGreetings: false));

        HttpClient visitor = fixture.CreateClient();
        JsonElement theirs = await ReadAsync(await visitor.GetAsync(WebChatApiFixture.Relative($"/api/public/webchat/{rivalKey}?lang=ar")));
        Assert.Equal("Rival hello", theirs.GetProperty("greeting").GetString());

        HttpResponseMessage unknown = await visitor.GetAsync(WebChatApiFixture.Relative("/api/public/webchat/AAAAAAAAAAAAAAAAAAAAAA?lang=ar"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(["status", "title", "traceId", "type"], Names(await ReadAsync(unknown)));
    }

    // ---- helpers --------------------------------------------------------------

    private async Task<(HttpClient Owner, Guid TenantId, Guid EndpointId, string PublicKey)> EndpointAsync(string name = "Greeting Shop")
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync(name);
        (string publicKey, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Helper");

        return (owner, tenantId, endpointId, publicKey);
    }

    private static object Body(string? greeting, object? greetings, bool includeGreetings = true) =>
        includeGreetings
            ? new
            {
                greeting,
                accentColor = "#123456",
                launcherPosition = "Right",
                theme = "Light",
                allowedOrigins = new[] { Website },
                greetings,
            }
            : new
            {
                greeting,
                accentColor = "#123456",
                launcherPosition = "Right",
                theme = "Light",
                allowedOrigins = new[] { Website },
            };

    private static Uri WidgetUri(Guid tenantId, Guid endpointId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat/{endpointId}/widget");

    private static HttpRequestMessage Embed(string publicKey, string query)
    {
        HttpRequestMessage request = new(HttpMethod.Get, WebChatApiFixture.Relative($"/api/public/embed/{publicKey}{query}"));
        request.Headers.TryAddWithoutValidation("X-OmniAI-Embed-Origin", Website);

        return request;
    }

    private static async Task<JsonElement> ReadWidgetAsync(HttpClient client, Guid tenantId, Guid endpointId) =>
        await ReadAsync(await client.GetAsync(WidgetUri(tenantId, endpointId)));

    private static async Task<HttpResponseMessage> WriteAsync(HttpClient owner, Guid tenantId, Guid endpointId, object body)
    {
        HttpResponseMessage read = await owner.GetAsync(WidgetUri(tenantId, endpointId));

        return await PutAsync(owner, tenantId, endpointId, body, read.Headers.ETag!.ToString());
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid tenantId, Guid endpointId, object body, string? ifMatch)
    {
        using HttpRequestMessage write = new(HttpMethod.Put, WidgetUri(tenantId, endpointId))
        {
            Content = JsonContent.Create(body, body.GetType()),
        };

        if (ifMatch is not null)
        {
            write.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(write);
    }

    private static string[] Names(JsonElement body) =>
        [.. body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
