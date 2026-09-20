using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Plans;

/// <summary>
/// The Plan and usage screen, over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Entitlement is not permission, and this suite holds the line.</strong>
/// A member who may create agents is still refused when the workspace is full,
/// and somebody who may not read the plan is refused before any count is taken —
/// a refusal that leaked "5 of 5 agents" would tell an outsider how busy a
/// workspace is.
/// </para>
/// <para>
/// <strong>This is not billing.</strong> Nothing on the wire names a price, a
/// payment or a plan to buy, and a test below sweeps the response for that
/// vocabulary.
/// </para>
/// </remarks>
public sealed class PlanUsageApiTests(PlanApiFixture fixture) : IClassFixture<PlanApiFixture>
{
    // ------------------------------------------------------------- who may ask

    [Fact]
    public async Task An_anonymous_request_is_refused()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient anonymous = fixture.CreateClient();

        using HttpResponseMessage response = await anonymous.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Somebody_who_is_not_a_member_is_told_nothing_exists()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient outsider = await fixture.CreateOtherTenantUserAsync();

        using HttpResponseMessage response = await outsider.GetAsync(Url(tenantId));

        // Not 403, and nothing about how full the workspace is: somebody outside
        // it learns only that there is nothing here for them.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("aiAgents", body, StringComparison.Ordinal);
        Assert.DoesNotContain("planCode", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_without_analytics_view_is_refused_without_learning_the_counts()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient member =
            await fixture.CreateRestrictedMemberAsync(tenantId, "ai-agents.create");

        using HttpResponseMessage response = await member.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("aiAgents", body, StringComparison.Ordinal);
        Assert.DoesNotContain("used", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_member_holding_analytics_view_reads_it()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpClient member =
            await fixture.CreateRestrictedMemberAsync(tenantId, "analytics.view");

        using HttpResponseMessage response = await member.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_reads_it_without_being_granted_anything()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------------ what is sent

    [Fact]
    public async Task A_new_workspace_is_on_the_configured_default_plan()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement body = await ReadAsync(owner, tenantId);

        // The plan named in configuration, never one the request chose.
        Assert.Equal("development", body.GetProperty("planCode").GetString());
        Assert.Equal(6, body.GetProperty("entitlements").GetArrayLength());
    }

    [Fact]
    public async Task An_unlimited_entitlement_is_sent_as_absent_rather_than_as_a_big_number()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement agents = Entitlement(await ReadAsync(owner, tenantId), "aiAgents");

        Assert.True(agents.GetProperty("unlimited").GetBoolean());
        Assert.Equal(JsonValueKind.Null, agents.GetProperty("limit").ValueKind);
        Assert.Equal(JsonValueKind.Null, agents.GetProperty("percentUsed").ValueKind);
        Assert.Equal("Normal", agents.GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_finite_plan_reports_usage_against_its_limit()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(tenantId, agents: 4);

        await PlanApiFixture.CreateAgentAsync(owner, tenantId);
        await PlanApiFixture.CreateAgentAsync(owner, tenantId);
        await PlanApiFixture.CreateAgentAsync(owner, tenantId);

        JsonElement agents = Entitlement(await ReadAsync(owner, tenantId), "aiAgents");

        Assert.Equal(3, agents.GetProperty("used").GetInt64());
        Assert.Equal(4, agents.GetProperty("limit").GetInt32());
        Assert.Equal(75, agents.GetProperty("percentUsed").GetInt32());
        Assert.Equal(1, agents.GetProperty("remaining").GetInt64());
        Assert.Equal("Warning", agents.GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_monthly_entitlement_carries_its_utc_month()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement operations =
            Entitlement(await ReadAsync(owner, tenantId), "monthlyProviderOperations");

        JsonElement period = operations.GetProperty("period");

        DateTimeOffset from = period.GetProperty("from").GetDateTimeOffset();
        DateTimeOffset to = period.GetProperty("to").GetDateTimeOffset();

        Assert.Equal(1, from.Day);
        Assert.Equal(TimeSpan.Zero, from.Offset);
        Assert.Equal(from.AddMonths(1), to);
    }

    [Fact]
    public async Task One_workspace_never_sees_another_workspaces_plan()
    {
        (HttpClient mine, Guid myTenant, _) = await fixture.CreateOwnerAsync();
        (HttpClient theirs, Guid theirTenant, _) = await fixture.CreateOwnerAsync();

        await fixture.MoveToPlanAsync(myTenant, agents: 1);

        await PlanApiFixture.CreateAgentAsync(mine, myTenant);

        Assert.Equal(
            1,
            Entitlement(await ReadAsync(mine, myTenant), "aiAgents").GetProperty("used").GetInt64());

        // Their workspace is untouched by mine being full, and mine cannot be
        // read by asking for theirs.
        Assert.Equal(
            0,
            Entitlement(await ReadAsync(theirs, theirTenant), "aiAgents")
                .GetProperty("used").GetInt64());

        using HttpResponseMessage crossed = await mine.GetAsync(Url(theirTenant));

        Assert.Equal(HttpStatusCode.NotFound, crossed.StatusCode);
    }

    // -------------------------------------------------------- not billing

    [Fact]
    public async Task Nothing_on_the_wire_claims_a_price_or_a_payment()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        string raw = await response.Content.ReadAsStringAsync();

        foreach (string forbidden in new[]
        {
            "price", "amount", "invoice", "subscription", "payment", "card",
            "currency", "vat", "tax", "credit", "wallet", "upgrade", "checkout",
            "refund", "billing",
        })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ----------------------------------------------------------------- safety

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_workspace_cannot_write_its_own_plan(string method)
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        using HttpRequestMessage request = new(new HttpMethod(method), Url(tenantId));

        using HttpResponseMessage response = await owner.SendAsync(request);

        // There is no write route under this path and none is intended: a
        // workspace that could edit its limits would not have limits.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task There_is_no_plan_administration_endpoint()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        foreach (string path in new[]
        {
            "/api/plans",
            $"/api/tenants/{tenantId}/plans",
            $"/api/tenants/{tenantId}/plan",
            $"/api/tenants/{tenantId}/plan-usage/upgrade",
        })
        {
            using HttpRequestMessage request = new(HttpMethod.Post, path);

            using HttpResponseMessage response = await owner.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"'{path}' answered {(int)response.StatusCode}, so something writable exists there.");
        }
    }

    private static string Url(Guid tenantId) => $"/api/tenants/{tenantId}/plan-usage";

    private static JsonElement Entitlement(JsonElement body, string name)
    {
        foreach (JsonElement entitlement in body.GetProperty("entitlements").EnumerateArray())
        {
            if (entitlement.GetProperty("entitlement").GetString() == name)
            {
                return entitlement;
            }
        }

        Assert.Fail($"The response carries no '{name}' entitlement.");

        return default;
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid tenantId)
    {
        using HttpResponseMessage response = await client.GetAsync(Url(tenantId));

        response.EnsureSuccessStatusCode();

        using JsonDocument document =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}

/// <summary>
/// A hosted application whose workspaces can be moved onto a finite plan.
/// </summary>
/// <remarks>
/// <strong>The move is done in the database, not through an endpoint.</strong>
/// There is deliberately no route that changes a workspace's plan — that is
/// Phase 25's, along with whatever authority is meant to approve it — so a test
/// that needs a finite plan writes the assignment directly, exactly as an
/// operator would.
/// </remarks>
public sealed class PlanApiFixture : RoleApiFixture
{
    /// <summary>Puts a workspace on a new plan with the given caps.</summary>
    public async Task MoveToPlanAsync(
        Guid tenantId,
        int? members = null,
        int? agents = null,
        int? documents = null,
        int? webChat = null,
        int? whatsApp = null,
        int? monthlyOperations = null)
    {
        Plan plan = Plan.Create(
            PlanCode.Create($"p{Guid.NewGuid():N}"[..20]),
            PlanDisplayName.Create("Test plan"),
            PlanLimit.From(members),
            PlanLimit.From(agents),
            PlanLimit.From(documents),
            PlanLimit.From(webChat),
            PlanLimit.From(whatsApp),
            PlanLimit.From(monthlyOperations),
            DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = CreateDbContext();

        context.Plans.Add(plan);

        await context.SaveChangesAsync();

        await context.WorkspacePlanAssignments
            .Where(assignment => assignment.TenantId == TenantId.From(tenantId))
            .ExecuteDeleteAsync();

        context.WorkspacePlanAssignments.Add(
            WorkspacePlanAssignment.Create(
                TenantId.From(tenantId),
                plan.Id,
                DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();
    }

    /// <summary>Seeds provider operations for a workspace, as the ledger would.</summary>
    public async Task AddProviderOperationsAsync(Guid tenantId, int count)
    {
        await using OmniAiDbContext context = CreateDbContext();

        for (int index = 0; index < count; index++)
        {
            context.AiProviderUsages.Add(Domain.Ai.AiProviderUsage.Begin(
                TenantId.From(tenantId),
                Domain.Ai.AiProvider.OpenAi,
                Domain.Ai.AiOperationType.TextGeneration,
                Domain.Ai.AiUsagePurpose.ConversationGeneration,
                Domain.Ai.ModelName.Create("test-model-x"),
                DateTimeOffset.UtcNow));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Creates an agent through the real endpoint.</summary>
    public static async Task<HttpResponseMessage> CreateAgentAsync(HttpClient client, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/ai-agents",
            new { name = $"Agent {Guid.NewGuid():N}"[..20], instructions = "Be helpful." });

        return response;
    }
}
