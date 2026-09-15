using System.Net;
using System.Text.Json;
using OmniAI.Domain.Ai;

namespace OmniAI.Api.IntegrationTests.Analytics;

/// <summary>
/// The provider usage section of the dashboard, over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Internal accounting, not billing.</strong> This section reports what
/// this platform estimates it paid providers on a workspace's behalf. There is no
/// invoice here, no customer charge, no amount due and no subscription — and the
/// wording test below is written to keep it that way, because a field called
/// <c>amountDue</c> would be read as one the day it appeared.
/// </para>
/// <para>
/// The arithmetic is proved against real rows in the Infrastructure suite. What
/// is proved here is the permission it hides behind, the shape on the wire, and
/// that nothing about a prompt, a customer or a provider credential travels in
/// it.
/// </para>
/// </remarks>
public sealed class UsageAnalyticsApiTests(AnalyticsApiFixture fixture)
    : IClassFixture<AnalyticsApiFixture>
{
    // ------------------------------------------------------------- who may ask

    /// <remarks>
    /// Deliberately the same permission as the rest of analytics. A separate
    /// <c>usage.view</c> or <c>cost.view</c> would be a second thing for an
    /// administrator to get right, and getting it wrong would expose the same
    /// numbers.
    /// </remarks>
    [Fact]
    public async Task Usage_hides_behind_the_analytics_permission_and_no_other()
    {
        (_, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId);

        using HttpClient allowed =
            await fixture.CreateRestrictedMemberAsync(tenantId, "analytics.view");

        JsonElement body = await ReadAsync(allowed, tenantId);

        Assert.True(body.TryGetProperty("aiUsage", out JsonElement usage));
        Assert.Equal(JsonValueKind.Object, usage.ValueKind);

        using HttpClient refused =
            await fixture.CreateRestrictedMemberAsync(tenantId, "conversations.view");

        using HttpResponseMessage response = await refused.GetAsync(Url(tenantId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task One_workspace_never_sees_another_workspaces_spend()
    {
        (HttpClient mine, Guid myTenant, _) = await fixture.CreateOwnerAsync();
        (HttpClient theirs, Guid theirTenant, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(myTenant, inputTokens: 1_000_000, outputTokens: 0);

        for (int index = 0; index < 4; index++)
        {
            await fixture.SeedUsageAsync(theirTenant, inputTokens: 1_000_000, outputTokens: 0);
        }

        Assert.Equal(1, Usage(await ReadAsync(mine, myTenant)).GetProperty("calls").GetInt64());
        Assert.Equal(
            4,
            Usage(await ReadAsync(theirs, theirTenant)).GetProperty("calls").GetInt64());
    }

    // ------------------------------------------------------------ what is sent

    [Fact]
    public async Task A_workspace_that_never_called_a_provider_gets_no_usage_section()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        JsonElement body = await ReadAsync(owner, tenantId);

        // Absent, not a panel of noughts. A workspace with no provider calls has
        // nothing to report, and "0.00" would claim it had been measured.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("aiUsage").ValueKind);
        Assert.Empty(body.GetProperty("models").EnumerateArray());
    }

    [Fact]
    public async Task The_section_reports_calls_tokens_and_an_estimated_cost()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId, inputTokens: 1_000_000, outputTokens: 1_000_000);

        JsonElement usage = Usage(await ReadAsync(owner, tenantId));

        Assert.Equal(1, usage.GetProperty("calls").GetInt64());
        Assert.Equal(1, usage.GetProperty("generationCalls").GetInt64());
        Assert.Equal(1, usage.GetProperty("succeeded").GetInt64());
        Assert.Equal(1_000_000, usage.GetProperty("generationInputTokens").GetInt64());
        Assert.Equal(1_000_000, usage.GetProperty("generationOutputTokens").GetInt64());

        JsonElement cost = Assert.Single(usage.GetProperty("ratedCost").EnumerateArray());

        // 1M in at 2.00 plus 1M out at 8.00, carried as an exact decimal.
        Assert.Equal("USD", cost.GetProperty("currency").GetString());
        Assert.Equal(10.00m, cost.GetProperty("estimatedCost").GetDecimal());
    }

    /// <remarks>
    /// <strong>Unrated is not free.</strong> A call nobody configured a price for
    /// still happened and still cost money; sending it as a cost of zero would
    /// make the total quietly wrong.
    /// </remarks>
    [Fact]
    public async Task A_call_with_no_configured_rate_is_reported_as_unrated_and_not_as_free()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(
            tenantId,
            model: "test-model-unpriced",
            inputTokens: 5_000,
            inputPerMillion: null);

        JsonElement usage = Usage(await ReadAsync(owner, tenantId));

        Assert.Equal(1, usage.GetProperty("calls").GetInt64());
        Assert.Equal(1, usage.GetProperty("unratedCalls").GetInt64());

        // No currency total at all, rather than one reading nought.
        Assert.Empty(usage.GetProperty("ratedCost").EnumerateArray());

        // The tokens are still there. Not knowing the price is not the same as
        // not knowing the usage.
        Assert.Equal(5_000, usage.GetProperty("generationInputTokens").GetInt64());
    }

    [Fact]
    public async Task A_call_the_provider_reported_nothing_for_sends_no_token_figure()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId, inputTokens: null, outputTokens: null);

        JsonElement usage = Usage(await ReadAsync(owner, tenantId));

        // The call is counted. Its tokens are unknown, which shows as nothing
        // added rather than a zero somebody would read as "used none".
        Assert.Equal(1, usage.GetProperty("calls").GetInt64());
        Assert.Equal(0, usage.GetProperty("generationInputTokens").GetInt64());
    }

    [Fact]
    public async Task Outcomes_nobody_resolved_are_reported_rather_than_dropped()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId, status: AiOperationStatus.Failed);
        await fixture.SeedUsageAsync(tenantId, status: AiOperationStatus.Unknown);
        await fixture.SeedUsageAsync(tenantId, status: AiOperationStatus.Started);

        JsonElement usage = Usage(await ReadAsync(owner, tenantId));

        Assert.Equal(3, usage.GetProperty("calls").GetInt64());
        Assert.Equal(1, usage.GetProperty("failed").GetInt64());

        // Unknown and never-settled together: the provider may well have been
        // paid for both, and hiding them would understate the spend.
        Assert.Equal(2, usage.GetProperty("unresolved").GetInt64());
    }

    [Fact]
    public async Task Currencies_are_sent_apart_and_never_added_together()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(
            tenantId, inputTokens: 1_000_000, outputTokens: 0, currency: "USD");

        await fixture.SeedUsageAsync(
            tenantId,
            model: "test-model-eu",
            inputTokens: 1_000_000,
            outputTokens: 0,
            currency: "EUR");

        JsonElement usage = Usage(await ReadAsync(owner, tenantId));

        List<JsonElement> costs = [.. usage.GetProperty("ratedCost").EnumerateArray()];

        // Two totals. There is no exchange rate in this platform and there must
        // not be one: a converted figure would be a fiction.
        Assert.Equal(2, costs.Count);
        Assert.Contains(costs, cost => cost.GetProperty("currency").GetString() == "EUR");
        Assert.Contains(costs, cost => cost.GetProperty("currency").GetString() == "USD");
    }

    [Fact]
    public async Task The_model_breakdown_names_models_and_nothing_else()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId, model: "test-model-x");
        await fixture.SeedUsageAsync(
            tenantId,
            model: "test-embed-a",
            operationType: AiOperationType.Embedding,
            purpose: AiUsagePurpose.KnowledgeIndexEmbedding,
            outputTokens: null);

        List<JsonElement> models =
            [.. (await ReadAsync(owner, tenantId)).GetProperty("models").EnumerateArray()];

        Assert.Equal(2, models.Count);

        Assert.All(models, model =>
        {
            // A published model identifier and counts. Nothing that could
            // identify a person, a conversation or a document.
            Assert.Equal("OpenAi", model.GetProperty("provider").GetString());
            Assert.False(string.IsNullOrWhiteSpace(model.GetProperty("model").GetString()));
            Assert.True(model.GetProperty("calls").GetInt64() > 0);
        });

        Assert.Contains(models, model =>
            model.GetProperty("operationType").GetString() == "Embedding");
    }

    // ----------------------------------------------------------------- privacy

    /// <remarks>
    /// The list is the spec's, read back off the wire. Every item on it is
    /// something that exists somewhere in this platform and must never reach a
    /// dashboard.
    /// </remarks>
    [Fact]
    public async Task Nothing_private_travels_in_the_usage_section()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedAsync(
            tenantId,
            "Support",
            customerMessages: 2,
            aiMessages: 1,
            text: "My number is +15551234567 and my card is 4111 1111 1111 1111");

        await fixture.SeedUsageAsync(tenantId);

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        string raw = await response.Content.ReadAsStringAsync();

        foreach (string forbidden in new[]
        {
            "4111", "+1555", "prompt", "completion", "instruction", "grounding",
            "excerpt", "vector", "apiKey", "api_key", "sk-",
            "authorization", "bearer", "secret", "credential", "password",
        })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }

        // "Embedding" itself is allowed, because it names a kind of operation.
        // What must never appear is an embedding's contents.
        Assert.DoesNotContain("embeddingVector", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("embeddingCalls", raw, StringComparison.Ordinal);
    }

    /// <remarks>
    /// <strong>Wording is a contract here.</strong> Phase 23 is internal
    /// accounting; a field named for billing would be treated as billing by the
    /// next person to read it, whatever the documentation says.
    /// </remarks>
    [Fact]
    public async Task Nothing_on_the_wire_claims_to_be_a_bill()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId);

        using HttpResponseMessage response = await owner.GetAsync(Url(tenantId));

        string raw = await response.Content.ReadAsStringAsync();

        foreach (string forbidden in new[]
        {
            "invoice", "amountDue", "amount_due", "balance", "credit", "wallet",
            "subscription", "plan\"", "quota", "limit", "markup", "vat", "tax",
            "charge", "payment", "customerCost", "billing",
        })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }

        // And the figure that does travel says what it is.
        Assert.Contains("estimatedCost", raw, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ safety

    [Fact]
    public async Task Reading_usage_makes_no_provider_call_of_its_own()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        await fixture.SeedUsageAsync(tenantId);

        fixture.Embeddings.Reset();

        JsonElement body = await ReadAsync(owner, tenantId);

        // Accounting for spend must not be a way of spending. The provider double
        // throws the moment it is reached.
        Assert.Equal(0, fixture.Embeddings.Calls);

        Assert.Equal(1, Usage(body).GetProperty("calls").GetInt64());
    }

    [Fact]
    public async Task Money_reaches_the_wire_as_a_number_and_not_as_a_float()
    {
        (HttpClient owner, Guid tenantId, _) = await fixture.CreateOwnerAsync();

        // 1,234 tokens at 2.00 per million: 0.002468, which no binary float
        // represents exactly.
        await fixture.SeedUsageAsync(tenantId, inputTokens: 1_234, outputTokens: null);

        JsonElement cost = Assert.Single(
            Usage(await ReadAsync(owner, tenantId)).GetProperty("ratedCost").EnumerateArray());

        // Serialised from a decimal, so the digits on the wire are the digits
        // that were computed — not 0.0024680000000000001.
        Assert.Equal("0.002468", cost.GetProperty("estimatedCost").GetRawText());
    }

    // ----------------------------------------------------------------- helpers

    private static JsonElement Usage(JsonElement body)
    {
        JsonElement usage = body.GetProperty("aiUsage");

        Assert.Equal(JsonValueKind.Object, usage.ValueKind);

        return usage;
    }

    private static string Url(Guid tenantId) =>
        $"/api/tenants/{tenantId}/analytics/dashboard";

    private static async Task<JsonElement> ReadAsync(HttpClient client, Guid tenantId)
    {
        using HttpResponseMessage response = await client.GetAsync(Url(tenantId));

        response.EnsureSuccessStatusCode();

        using JsonDocument document =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }
}
