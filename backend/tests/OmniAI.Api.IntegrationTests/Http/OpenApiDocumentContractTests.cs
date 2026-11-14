using System.Net;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.WebChat;

namespace OmniAI.Api.IntegrationTests.Http;

/// <summary>
/// Phase 38.7b (BA-4.1, BA-4.2, BA-5, BA-12): the OpenAPI document describes what
/// the API really answers, read from the test host so it cannot drift back.
/// </summary>
/// <remarks>
/// Metadata only: every check here is about the document. The behaviour it
/// describes is pinned by each area's own API tests.
/// </remarks>
public sealed class OpenApiDocumentContractTests(WebChatApiFixture fixture)
    : IClassFixture<WebChatApiFixture>
{
    /// <summary>Writes that answer a provider or a probe, not the application.</summary>
    private static readonly HashSet<string> ProviderFacing =
    [
        "/api/billing/webhook",
        "/api/whatsapp/webhook",
    ];

    [Fact]
    public async Task The_invitation_create_declares_201_with_its_body()
    {
        JsonElement responses = Operation(await DocumentAsync(), "/api/tenants/{tenantId}/invitations", "post")
            .GetProperty("responses");

        Assert.Equal("CreatedInvitationResponse", SchemaOf(responses, "201"));
        Assert.False(responses.TryGetProperty("200", out _));
    }

    [Fact]
    public async Task The_role_writes_declare_the_role_without_its_member_count()
    {
        JsonElement document = await DocumentAsync();

        Assert.Equal("RoleResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/roles", "post").GetProperty("responses"), "201"));
        Assert.Equal("RoleResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/roles/{roleId}/name", "put").GetProperty("responses"), "200"));
        Assert.Equal("RoleResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/roles/{roleId}/permissions", "put").GetProperty("responses"), "200"));

        JsonElement role = Schema(document, "RoleResponse");
        Assert.False(role.GetProperty("properties").TryGetProperty("memberCount", out _));
    }

    [Fact]
    public async Task The_problem_body_declares_its_extension_members()
    {
        JsonElement properties = Schema(await DocumentAsync(), "ProblemDetails").GetProperty("properties");

        foreach (string member in new[] { "code", "codes", "limit", "limitUnit", "entitlement", "current", "periodFrom", "periodTo" })
        {
            Assert.True(properties.TryGetProperty(member, out _), $"ProblemDetails has no '{member}'.");
        }

        string[] units = [.. properties.GetProperty("limitUnit").GetProperty("enum").EnumerateArray().Select(unit => unit.GetString()!)];
        Assert.Contains("entries", units);
        Assert.Contains("bytes", units);
    }

    [Fact]
    public async Task The_invitation_list_declares_its_status_parameter()
    {
        JsonElement parameters = Operation(await DocumentAsync(), "/api/tenants/{tenantId}/invitations", "get")
            .GetProperty("parameters");

        JsonElement status = parameters.EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "status");

        Assert.Equal("query", status.GetProperty("in").GetString());
        Assert.False(status.TryGetProperty("required", out JsonElement required) && required.GetBoolean());
    }

    [Fact]
    public async Task Members_written_only_with_a_value_are_optional()
    {
        JsonElement document = await DocumentAsync();

        // Absent without compare=previous.
        AssertOptional(Schema(document, "AnalyticsDashboardResponse"), "previous");

        // Absent for anybody without workspace.manage, and on every write.
        AssertOptional(Schema(document, "TenantResponse"), "scheduleCount");
    }

    [Fact]
    public async Task The_workspace_list_declares_its_body()
    {
        JsonElement ok = Operation(await DocumentAsync(), "/api/me/tenants", "get")
            .GetProperty("responses").GetProperty("200");

        JsonElement schema = ok.GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.Equal("array", schema.GetProperty("type").GetString());
        Assert.EndsWith("/WorkspaceResponse", schema.GetProperty("items").GetProperty("$ref").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_customer_route_declares_its_body()
    {
        JsonElement document = await DocumentAsync();

        Assert.Equal("CustomerPageResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/customers", "get").GetProperty("responses"), "200"));
        Assert.Equal("CustomerResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/customers/{customerId}", "get").GetProperty("responses"), "200"));
        Assert.Equal("CustomerResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/customers/{customerId}/display-name", "put").GetProperty("responses"), "200"));
        Assert.Equal("CustomerMergeResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/customers/{customerId}/merge", "post").GetProperty("responses"), "200"));
    }

    [Fact]
    public async Task No_write_answers_a_success_the_document_does_not_describe()
    {
        List<string> undescribed = [];

        foreach (JsonProperty path in (await DocumentAsync()).GetProperty("paths").EnumerateObject())
        {
            if (ProviderFacing.Contains(path.Name))
            {
                continue;
            }

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (operation.Name is not ("post" or "put" or "delete"))
                {
                    continue;
                }

                foreach (JsonProperty response in operation.Value.GetProperty("responses").EnumerateObject())
                {
                    // A 2xx is described when it carries a body schema, or when it
                    // is one that has no body: 202 and 204.
                    if (response.Name.StartsWith('2')
                        && response.Name is not ("202" or "204")
                        && !response.Value.TryGetProperty("content", out _))
                    {
                        undescribed.Add($"{operation.Name.ToUpperInvariant()} {path.Name} {response.Name}");
                    }
                }
            }
        }

        Assert.True(undescribed.Count == 0, "Writes with an undescribed success: " + string.Join(", ", undescribed));
    }

    [Fact]
    public async Task The_email_preferences_read_is_typed_exactly_and_the_save_keeps_its_nullable_members()
    {
        // Phase 39.1 (note 19): one schema served both sides, so the document
        // typed the answer as nullable although it is always true or false.
        JsonElement document = await DocumentAsync();
        string[] members = ["reports", "usage", "billing", "knowledge", "channelHealth"];

        JsonElement response = Schema(document, "EmailPreferencesResponse");
        string[] required = [.. response.GetProperty("required").EnumerateArray().Select(item => item.GetString()!)];

        foreach (string member in members)
        {
            Assert.Contains(member, required);
            Assert.Equal(["boolean"], Types(response.GetProperty("properties").GetProperty(member)));
        }

        JsonElement request = Schema(document, "EmailPreferencesRequest");

        foreach (string member in members)
        {
            Assert.Contains("null", Types(request.GetProperty("properties").GetProperty(member)));
        }

        Assert.EndsWith(
            "/EmailPreferencesResponse",
            Schema(document, "NotificationPreferencesResponse").GetProperty("properties").GetProperty("email").GetProperty("$ref").GetString(),
            StringComparison.Ordinal);
        Assert.False(document.GetProperty("components").GetProperty("schemas").TryGetProperty("EmailPreferencesContract", out _));
    }

    [Fact]
    public async Task The_phase_39_1_routes_and_members_are_described()
    {
        JsonElement document = await DocumentAsync();

        Assert.True(Operation(document, "/api/me/display-name", "put").GetProperty("responses").TryGetProperty("204", out _));
        Assert.Equal("MarkAllConversationsReadResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/conversations/read-all", "post").GetProperty("responses"), "200"));
        Assert.Equal("ConversationUnreadCountResponse", SchemaOf(Operation(document, "/api/tenants/{tenantId}/conversations/unread-count", "get").GetProperty("responses"), "200"));

        Assert.True(Schema(document, "ProblemDetails").GetProperty("properties").TryGetProperty("field", out _));

        JsonElement schemas = document.GetProperty("components").GetProperty("schemas");
        List<string> missing = [];

        foreach ((string schema, string member) in new[]
        {
            ("CurrentUserResponse", "displayName"),
            ("WorkspaceMemberResponse", "displayName"),
            ("MemberSummaryResponse", "displayName"),
            ("MemberResponse", "displayName"),
            ("InvitationResponse", "invitedByDisplayName"),
            ("WebChatWidgetResponse", "greetings"),
            ("UpdateWebChatWidgetRequest", "greetings"),
            ("CustomerRowResponse", "reference"),
            ("CustomerResponse", "reference"),
            ("AnalyticsSummaryResponse", "conversationsAnsweredByAi"),
        })
        {
            if (!schemas.TryGetProperty(schema, out JsonElement described)
                || !described.GetProperty("properties").TryGetProperty(member, out _))
            {
                missing.Add($"{schema}.{member}");
            }
        }

        Assert.True(
            missing.Count == 0,
            "Not described: " + string.Join(", ", missing)
                + ". Schemas: " + string.Join(", ", schemas.EnumerateObject().Select(entry => entry.Name)));

        // The public reads take an optional lang.
        foreach (string path in new[] { "/api/public/webchat/{publicKey}", "/api/public/embed/{publicKey}" })
        {
            JsonElement lang = Operation(document, path, "get").GetProperty("parameters").EnumerateArray()
                .Single(parameter => parameter.GetProperty("name").GetString() == "lang");

            Assert.Equal("query", lang.GetProperty("in").GetString());
            Assert.False(lang.TryGetProperty("required", out JsonElement required) && required.GetBoolean());
        }
    }

    [Fact]
    public async Task The_document_carries_the_public_name()
    {
        Assert.Equal("Nabra API", (await DocumentAsync()).GetProperty("info").GetProperty("title").GetString());
    }

    private async Task<JsonElement> DocumentAsync()
    {
        HttpClient client = await fixture.CreateSignedInClientAsync();
        HttpResponseMessage response = await client.GetAsync(WebChatApiFixture.Relative("/openapi/v1.json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static JsonElement Schema(JsonElement document, string name) =>
        document.GetProperty("components").GetProperty("schemas").GetProperty(name);

    private static string? SchemaOf(JsonElement responses, string status) =>
        responses.GetProperty(status)
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()?
            .Split('/')[^1];

    /// <summary>A member's JSON types, whether written as one name or a list (OpenAPI 3.1).</summary>
    private static string[] Types(JsonElement member) =>
        member.TryGetProperty("type", out JsonElement type)
            ? type.ValueKind == JsonValueKind.Array
                ? [.. type.EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal)]
                : [type.GetString()!]
            : [];

    private static void AssertOptional(JsonElement schema, string member)
    {
        Assert.True(schema.GetProperty("properties").TryGetProperty(member, out _), $"No '{member}' at all.");

        bool required = schema.TryGetProperty("required", out JsonElement list)
            && list.EnumerateArray().Any(item => item.GetString() == member);

        Assert.False(required, $"'{member}' is listed as required.");
    }
}
