using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.WebChat;

/// <summary>
/// Phase 37.5a, rows 19-22: the hosted page's appearance, the workspace name on
/// both public reads, coded admin origin refusals that leave the public refusals
/// bodiless, and the direction-aware corners.
/// </summary>
public sealed class PublicChatAppearanceApiTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    private const string Website = "https://shop.example.com";

    [Fact]
    public async Task The_hosted_read_carries_the_widgets_appearance_and_the_workspace_name()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync("Northwind Retail");
        (string publicKey, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId, "Helper");

        Assert.Equal(HttpStatusCode.OK, (await WriteWidgetAsync(owner, tenantId, endpointId, Widget("Left", Website))).StatusCode);

        JsonElement hosted = await ReadAsync(await fixture.CreateClient().GetAsync(Hosted(publicKey)));

        Assert.Equal("Helper", hosted.GetProperty("agentName").GetString());
        Assert.True(hosted.GetProperty("available").GetBoolean());
        Assert.Equal("Northwind Retail", hosted.GetProperty("workspaceName").GetString());
        Assert.Equal("Welcome — how can we help?", hosted.GetProperty("greeting").GetString());
        Assert.Equal("#123456", hosted.GetProperty("accentColor").GetString());
        Assert.Equal("Dark", hosted.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task An_endpoint_never_styled_reads_the_platform_defaults()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, _) = await WebChatApiFixture.PublishChatAsync(owner, tenantId);

        JsonElement hosted = await ReadAsync(await fixture.CreateClient().GetAsync(Hosted(publicKey)));

        Assert.Equal(JsonValueKind.Null, hosted.GetProperty("greeting").ValueKind);
        Assert.Equal("System", hosted.GetProperty("theme").GetString());
        Assert.StartsWith("#", hosted.GetProperty("accentColor").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_public_reads_publish_nothing_beyond_what_a_visitor_sees_and_the_name()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId);
        await WriteWidgetAsync(owner, tenantId, endpointId, Widget("Right", Website));

        HttpClient visitor = fixture.CreateClient();

        Assert.Equal(
            ["accentColor", "agentName", "available", "greeting", "theme", "workspaceName"],
            Names(await ReadAsync(await visitor.GetAsync(Hosted(publicKey)))));

        Assert.Equal(
            ["accentColor", "agentName", "greeting", "isAvailable", "launcherPosition", "theme", "workspaceName"],
            Names(await ReadAsync(await visitor.SendAsync(EmbedRequest(publicKey, Website)))));
    }

    [Fact]
    public async Task An_unknown_key_is_still_the_generic_404_on_both_public_reads()
    {
        HttpClient visitor = fixture.CreateClient();
        const string Unknown = "AAAAAAAAAAAAAAAAAAAAAA";

        HttpResponseMessage hosted = await visitor.GetAsync(Hosted(Unknown));
        HttpResponseMessage embedded = await visitor.SendAsync(EmbedRequest(Unknown, Website));

        // The pipeline's generic problem and nothing more: no detail, and no code
        // that could tell one reason from another (Phase 37.5a keeps it uncoded).
        foreach (HttpResponseMessage response in new[] { hosted, embedded })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(["status", "title", "traceId", "type"], Names(await ReadAsync(response)));
        }
    }

    [Fact]
    public async Task An_unapproved_website_is_answered_exactly_like_an_unknown_key()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId);
        await WriteWidgetAsync(owner, tenantId, endpointId, Widget("Right", Website));

        HttpClient visitor = fixture.CreateClient();

        HttpResponseMessage unapproved = await visitor.SendAsync(EmbedRequest(publicKey, "https://elsewhere.example.com"));
        HttpResponseMessage unknown = await visitor.SendAsync(EmbedRequest("AAAAAAAAAAAAAAAAAAAAAA", Website));

        // Byte for byte the same, once the per-request trace id is set aside.
        Assert.Equal(unknown.StatusCode, unapproved.StatusCode);
        Assert.Equal(await WithoutTraceAsync(unknown), await WithoutTraceAsync(unapproved));
        Assert.Equal(unknown.Content.Headers.ContentType, unapproved.Content.Headers.ContentType);
    }

    [Theory]
    [InlineData("Start")]
    [InlineData("End")]
    [InlineData("Left")]
    [InlineData("Right")]
    public async Task Each_corner_round_trips_through_the_admin_and_the_embedded_read(string position)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (string publicKey, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId);

        Assert.Equal(HttpStatusCode.OK, (await WriteWidgetAsync(owner, tenantId, endpointId, Widget(position, Website))).StatusCode);

        JsonElement admin = await ReadAsync(await owner.GetAsync(WidgetUri(tenantId, endpointId)));
        JsonElement embedded = await ReadAsync(await fixture.CreateClient().SendAsync(EmbedRequest(publicKey, Website)));

        Assert.Equal(position, admin.GetProperty("widget").GetProperty("launcherPosition").GetString());
        Assert.Equal(position, embedded.GetProperty("launcherPosition").GetString());
    }

    [Theory]
    [InlineData("website-chat.origin.invalid", "path")]
    [InlineData("website-chat.origin.invalid", "wildcard")]
    [InlineData("website-chat.origin.too_many", "too-many")]
    [InlineData("website-chat.launcher_position.invalid", "corner")]
    [InlineData("website-chat.theme.invalid", "theme")]
    [InlineData("website-chat.accent_color.invalid", "accent")]
    public async Task An_admin_widget_refusal_is_a_400_with_its_stable_code(string code, string refusal)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();
        (_, Guid endpointId) = await WebChatApiFixture.PublishChatAsync(owner, tenantId);

        object body = refusal switch
        {
            "path" => WidgetBody("Right", ["https://shop.example.com/path"]),
            "wildcard" => WidgetBody("Right", ["*.example.com"]),
            "too-many" => WidgetBody("Right", [.. Enumerable.Range(0, 21).Select(n => $"https://site{n}.example.com")]),
            "corner" => WidgetBody("Middle", []),
            "theme" => new { accentColor = "#2563eb", launcherPosition = "Right", theme = "Neon", allowedOrigins = Array.Empty<string>() },
            _ => new { accentColor = "red", launcherPosition = "Right", theme = "System", allowedOrigins = Array.Empty<string>() },
        };

        HttpResponseMessage refused = await WriteWidgetAsync(owner, tenantId, endpointId, body);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(code, (await ReadAsync(refused)).GetProperty("code").GetString());
    }

    private static object Widget(string position, params string[] origins) => WidgetBody(position, origins);

    private static object WidgetBody(string position, string[] origins) =>
        new
        {
            greeting = "Welcome — how can we help?",
            accentColor = "#123456",
            launcherPosition = position,
            theme = "Dark",
            allowedOrigins = origins,
        };

    private static Uri Hosted(string publicKey) => WebChatApiFixture.Relative($"/api/public/webchat/{publicKey}");

    private static Uri WidgetUri(Guid tenantId, Guid endpointId) =>
        WebChatApiFixture.Relative($"/api/tenants/{tenantId}/website-chat/{endpointId}/widget");

    private static HttpRequestMessage EmbedRequest(string publicKey, string origin)
    {
        HttpRequestMessage request = new(HttpMethod.Get, WebChatApiFixture.Relative($"/api/public/embed/{publicKey}"));
        request.Headers.TryAddWithoutValidation("X-OmniAI-Embed-Origin", origin);

        return request;
    }

    private static async Task<HttpResponseMessage> WriteWidgetAsync(HttpClient owner, Guid tenantId, Guid endpointId, object body)
    {
        HttpResponseMessage read = await owner.GetAsync(WidgetUri(tenantId, endpointId));

        HttpRequestMessage write = new(HttpMethod.Put, WidgetUri(tenantId, endpointId))
        {
            Content = JsonContent.Create(body, body.GetType()),
        };
        write.Headers.TryAddWithoutValidation("If-Match", read.Headers.ETag!.ToString());

        return await owner.SendAsync(write);
    }

    private static async Task<string> WithoutTraceAsync(HttpResponseMessage response) =>
        System.Text.RegularExpressions.Regex.Replace(
            await response.Content.ReadAsStringAsync(),
            "\"traceId\":\"[^\"]*\"",
            "\"traceId\":\"\"");

    private static string[] Names(JsonElement body) =>
        [.. body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
