using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Authorization;

/// <summary>
/// Which AI agent permission controls which endpoint, over real HTTP.
/// </summary>
/// <remarks>
/// Every case checks both directions: the granted operation succeeds, and a
/// neighbouring one is refused for the same member. A matrix that only proved
/// the first would pass just as happily if every permission granted everything.
/// </remarks>
public sealed class AiAgentPermissionApiTests(RoleApiFixture api) : IClassFixture<RoleApiFixture>
{
    [Fact]
    public async Task View_controls_listing_and_reading_and_nothing_more()
    {
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync();

        HttpClient viewer = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        using HttpResponseMessage list = await viewer.GetAsync($"/api/tenants/{tenantId}/ai-agents");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        using HttpResponseMessage read =
            await viewer.GetAsync($"/api/tenants/{tenantId}/ai-agents/{agentId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        // And nothing else.
        using HttpResponseMessage created = await viewer.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Another", instructions = "Be helpful." });
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        using HttpResponseMessage renamed = await ConditionalPutAsync(
            viewer,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name",
            new { name = "Renamed" },
            read.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Forbidden, renamed.StatusCode);
    }

    [Fact]
    public async Task Create_controls_creation_and_does_not_grant_viewing()
    {
        (Guid tenantId, _, _) = await ArrangeAsync();

        HttpClient creator = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.create");

        using HttpResponseMessage created = await creator.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Made by a creator", instructions = "Be helpful." });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Being able to add an agent does not confer the ability to browse them.
        using HttpResponseMessage list =
            await creator.GetAsync($"/api/tenants/{tenantId}/ai-agents");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Edit_controls_renaming_and_instructions_but_not_lifecycle()
    {
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync();

        HttpClient editor = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.edit");

        using HttpResponseMessage renamed = await ConditionalPutAsync(
            editor,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name",
            new { name = "Renamed by an editor" },
            version);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        using HttpResponseMessage instructions = await ConditionalPutAsync(
            editor,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/instructions",
            new { instructions = "Answer support questions." },
            renamed.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.OK, instructions.StatusCode);

        // Editing what an agent would say is not deciding whether it says
        // anything to customers at all.
        using HttpResponseMessage activated = await ConditionalPostAsync(
            editor,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/activate",
            instructions.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.Forbidden, activated.StatusCode);
    }

    [Fact]
    public async Task Lifecycle_controls_activation_and_disabling_but_not_editing()
    {
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync();

        HttpClient operatorClient =
            await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.lifecycle");

        using HttpResponseMessage activated = await ConditionalPostAsync(
            operatorClient,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/activate",
            version);

        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);

        using HttpResponseMessage disabled = await ConditionalPostAsync(
            operatorClient,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/disable",
            activated.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        using HttpResponseMessage renamed = await ConditionalPutAsync(
            operatorClient,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name",
            new { name = "Renamed" },
            disabled.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.Forbidden, renamed.StatusCode);
    }

    [Fact]
    public async Task Preview_only_can_preview_and_can_do_nothing_else()
    {
        // The agent id comes from setup, not from granting this member the
        // ability to look it up. Granting ai-agents.view to make the test
        // convenient would test a different thing entirely.
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync();

        HttpClient tester = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.preview");

        using HttpResponseMessage preview = await tester.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/preview",
            new { message = "Hello" });

        // 503 rather than 200 because no AI provider is configured in this test
        // deployment -- which is exactly the point: the request got past
        // authorisation and reached the runtime, rather than being refused.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, preview.StatusCode);

        using HttpResponseMessage list = await tester.GetAsync($"/api/tenants/{tenantId}/ai-agents");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        using HttpResponseMessage read =
            await tester.GetAsync($"/api/tenants/{tenantId}/ai-agents/{agentId}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        using HttpResponseMessage renamed = await ConditionalPutAsync(
            tester,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name",
            new { name = "Renamed" },
            version);
        Assert.Equal(HttpStatusCode.Forbidden, renamed.StatusCode);
    }

    [Fact]
    public async Task A_member_without_preview_is_refused_before_the_provider_is_reached()
    {
        // 403 rather than 503. The difference matters: 503 would mean the request
        // reached the runtime and found none configured, and a member without
        // this permission must not get that far -- it is the one place a request
        // costs money.
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync();

        HttpClient viewer = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        using HttpResponseMessage refused = await viewer.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/preview",
            new { message = "Hello" });

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task A_non_member_previewing_gets_not_found_rather_than_forbidden()
    {
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync();

        HttpClient stranger = await api.CreateOtherTenantUserAsync();

        using HttpResponseMessage refused = await stranger.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/preview",
            new { message = "Hello" });

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task A_denied_member_is_refused_identically_for_a_real_and_an_invented_agent()
    {
        // Authorisation runs before the agent is read, so a permission somebody
        // lacks cannot become a way to enumerate which agent ids are real.
        (Guid tenantId, Guid agentId, _) = await ArrangeAsync();

        HttpClient creator = await api.CreateRestrictedMemberAsync(tenantId, "ai-agents.create");

        using HttpResponseMessage real =
            await creator.GetAsync($"/api/tenants/{tenantId}/ai-agents/{agentId}");

        using HttpResponseMessage invented =
            await creator.GetAsync($"/api/tenants/{tenantId}/ai-agents/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.Forbidden, real.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, invented.StatusCode);

        // Identical bodies too, not merely identical statuses.
        //
        // traceId is excluded because it is per-request by definition: two
        // identical requests to the same route already carry different ones, so
        // it can say nothing about whether the agent exists. Every other field
        // is compared, and both bodies are checked to still carry a trace id --
        // dropping it from the comparison must not hide it going missing.
        Assert.Equal(
            await ProblemFieldsAsync(invented),
            await ProblemFieldsAsync(real));
    }

    /// <summary>
    /// A problem response's meaningful fields, with the per-request trace id
    /// left out and asserted present. Follows the convention already used by the
    /// preview tests for comparing two refusals that must be indistinguishable.
    /// </summary>
    private static async Task<string> ProblemFieldsAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.True(document.RootElement.TryGetProperty("traceId", out _));

        string[] fields =
        [
            .. document.RootElement.EnumerateObject()
                .Where(field => !string.Equals(field.Name, "traceId", StringComparison.Ordinal))
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .Select(field => $"{field.Name}={field.Value}")
        ];

        return string.Join('|', fields);
    }

    /// <summary>A workspace with one draft agent, and that agent's version.</summary>
    private async Task<(Guid TenantId, Guid AgentId, string Version)> ArrangeAsync()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();

        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = "Sales Assistant", instructions = "Answer sales questions." });

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        return (tenantId, body.RootElement.GetProperty("id").GetGuid(), created.Headers.ETag!.Tag);
    }

    private static async Task<HttpResponseMessage> ConditionalPutAsync(
        HttpClient client,
        string path,
        object payload,
        string ifMatch)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(payload),
        };

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(ifMatch));

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ConditionalPostAsync(
        HttpClient client,
        string path,
        string ifMatch)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path);

        request.Headers.IfMatch.Add(new EntityTagHeaderValue(ifMatch));

        return await client.SendAsync(request);
    }
}
