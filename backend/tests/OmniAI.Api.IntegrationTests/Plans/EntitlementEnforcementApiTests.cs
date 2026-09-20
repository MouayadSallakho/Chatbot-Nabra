using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Plans;

/// <summary>
/// What happens when a workspace reaches what its plan allows.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Permission and entitlement are different questions, asked in that
/// order.</strong> Somebody without the permission is refused before the plan is
/// ever consulted — otherwise a 409 saying "5 of 5 agents" would tell them how
/// busy a workspace they cannot touch is. Somebody with the permission is
/// refused by the plan, and no amount of role editing changes that.
/// </para>
/// <para>
/// <strong>409, and deliberately not the alternatives.</strong> Not 403, because
/// the caller is permitted; not 429, because waiting will not help; not 402,
/// because nothing here takes payment.
/// </para>
/// </remarks>
public sealed class EntitlementEnforcementApiTests(PlanApiFixture fixture)
    : IClassFixture<PlanApiFixture>
{
    // ------------------------------------------------- permission comes first

    [Fact]
    public async Task A_member_without_the_permission_is_refused_before_the_plan_is_consulted()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 1);

        await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        using HttpClient member =
            await fixture.CreateRestrictedMemberAsync(tenantId, "ai-agents.view");

        using HttpResponseMessage response =
            await PlanApiFixture.CreateAgentAsync(member, tenantId);

        // 403 rather than 409: the workspace is full, but this caller is not
        // entitled to learn that.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("entitlement", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("limit", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <remarks>
    /// The rule in one test: holding <c>ai-agents.create</c> is permission to try,
    /// not entitlement to succeed.
    /// </remarks>
    [Fact]
    public async Task A_member_who_may_create_agents_is_still_refused_when_the_workspace_is_full()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 1);

        await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        using HttpClient member =
            await fixture.CreateRestrictedMemberAsync(tenantId, "ai-agents.create");

        using HttpResponseMessage response =
            await PlanApiFixture.CreateAgentAsync(member, tenantId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------------------------------------------------------- the refusal

    [Fact]
    public async Task An_exhausted_entitlement_answers_409_with_a_stable_code()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 2);

        await PlanApiFixture.CreateAgentAsync(owner, tenantId);
        await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        using HttpResponseMessage response =
            await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using JsonDocument problem =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        JsonElement root = problem.RootElement;

        // The machine-readable half, so a browser never has to match on English.
        Assert.Equal("entitlement.aiagents.exhausted", root.GetProperty("code").GetString());
        Assert.Equal("aiAgents", root.GetProperty("entitlement").GetString());
        Assert.Equal(2, root.GetProperty("current").GetInt64());
        Assert.Equal(2, root.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task The_refusal_carries_no_commercial_claim()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 0);

        using HttpResponseMessage response =
            await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        // Phase 25 does not exist. Any of these would send somebody looking for
        // a screen that is not there.
        foreach (string forbidden in new[]
        {
            "payment required", "card", "declined", "subscription", "invoice",
            "upgrade", "price", "billing", "past due",
        })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_limit_of_zero_refuses_the_very_first_one()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 0);

        using HttpResponseMessage response =
            await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task An_unlimited_plan_refuses_nothing()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        for (int index = 0; index < 6; index++)
        {
            using HttpResponseMessage response =
                await PlanApiFixture.CreateAgentAsync(owner, tenantId);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }

    // ------------------------------------------------- existing work survives

    /// <remarks>
    /// Reaching the agent limit blocks creation and nothing else. A workspace
    /// that could no longer edit or disable what it already had would be worse
    /// off than one that had never filled up.
    /// </remarks>
    [Fact]
    public async Task A_full_workspace_can_still_edit_the_agents_it_has()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 1);

        using HttpResponseMessage created = await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        created.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());

        Guid agentId = body.RootElement.GetProperty("id").GetGuid();
        string version = created.Headers.ETag!.Tag;

        // Creation is refused...
        using HttpResponseMessage refused = await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // ...and renaming the existing one is not.
        using HttpRequestMessage rename = new(
            HttpMethod.Put,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/name")
        {
            Content = JsonContent.Create(new { name = "Renamed agent" }),
        };

        rename.Headers.TryAddWithoutValidation("If-Match", version);

        using HttpResponseMessage renamed = await owner.SendAsync(rename);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        // Nor is activating it. Reaching a count limit must not freeze the
        // workspace's existing configuration.
        using HttpRequestMessage activate = new(
            HttpMethod.Post,
            $"/api/tenants/{tenantId}/ai-agents/{agentId}/activate");

        activate.Headers.TryAddWithoutValidation("If-Match", renamed.Headers.ETag!.Tag);

        using HttpResponseMessage activated = await owner.SendAsync(activate);

        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
    }

    // ------------------------------------------------------------ concurrency

    /// <remarks>
    /// The same claim the Infrastructure suite proves against the service,
    /// re-proved end to end: two real HTTP requests at the last free slot.
    /// </remarks>
    [Fact]
    public async Task Two_concurrent_creates_at_the_last_slot_cannot_overshoot()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 3);

        await PlanApiFixture.CreateAgentAsync(owner, tenantId);
        await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        Task<HttpResponseMessage> first = PlanApiFixture.CreateAgentAsync(owner, tenantId);
        Task<HttpResponseMessage> second = PlanApiFixture.CreateAgentAsync(owner, tenantId);

        HttpResponseMessage[] responses = await Task.WhenAll(first, second);

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);

            using HttpResponseMessage listed =
                await owner.GetAsync($"/api/tenants/{tenantId}/ai-agents");

            listed.EnsureSuccessStatusCode();

            using JsonDocument agents =
                JsonDocument.Parse(await listed.Content.ReadAsStringAsync());

            Assert.Equal(3, agents.RootElement.GetProperty("items").GetArrayLength());
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }
}
