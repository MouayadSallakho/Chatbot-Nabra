using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Infrastructure.Time;
using OmniAI.Testing;
using static OmniAI.Api.IntegrationTests.AiAgents.AiAgentEndpointsTests;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// The time rules, proven end to end over HTTP with a clock the test controls.
/// </summary>
/// <remarks>
/// The other API suites run the real system clock, so they can only assert
/// relationships -- that one instant is not before another. These assert exact
/// values, and they are the only place the accepted high-water-mark rule can be
/// exercised through the whole stack: proving that an out-of-order instant still
/// applies the edit but does not drag <c>updatedAt</c> backwards requires moving
/// the clock <em>backwards</em>, which a real clock cannot do.
/// <para>
/// This is also what proves the API actually uses the injected
/// <see cref="TimeProvider"/> rather than calling <c>DateTimeOffset.UtcNow</c>
/// somewhere: if any handler read the ambient clock, the exact assertions below
/// would fail.
/// </para>
/// </remarks>
public sealed class AiAgentTimeApiTests(AiAgentTimeApiFixture api) : IClassFixture<AiAgentTimeApiFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_created_agent_carries_the_exact_instant_from_the_clock()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start;

        AgentPayload created = await ReadAgentAsync(
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules."));

        Assert.Equal(Start, created.CreatedAt);
        Assert.Equal(Start, created.UpdatedAt);
    }

    [Fact]
    public async Task The_stored_instants_match_what_the_response_reported()
    {
        // The create response is built from the aggregate in memory. If the two
        // ever disagreed, a client would see createdAt change on the next read
        // without anything having been edited.
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start;

        AgentPayload created = await ReadAgentAsync(
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules."));

        using HttpResponseMessage read = await client.GetAsync(AgentRoute(tenantId, created.Id));
        AgentPayload reloaded = await ReadAgentAsync(read);

        Assert.Equal(created.CreatedAt, reloaded.CreatedAt);
        Assert.Equal(created.UpdatedAt, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task A_rename_stamps_the_clock_instant_and_leaves_the_creation_instant_alone()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start;

        using HttpResponseMessage createdResponse =
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules.");
        AgentPayload created = await ReadAgentAsync(createdResponse);

        api.Clock.Now = Start.AddHours(1);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, created.Id)}/name",
            new { name = "Pre-sales Assistant" }, ETagOf(createdResponse));

        AgentPayload renamed = await ReadAgentAsync(response);

        Assert.Equal(Start.AddHours(1), renamed.UpdatedAt);
        Assert.Equal(Start, renamed.CreatedAt);
    }

    [Fact]
    public async Task Activation_stamps_the_clock_instant()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start;

        using HttpResponseMessage createdResponse =
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules.");
        AgentPayload created = await ReadAgentAsync(createdResponse);

        api.Clock.Now = Start.AddHours(2);

        (AgentPayload activated, _) =
            await ActAsync(client, tenantId, created.Id, "activate", ETagOf(createdResponse));

        Assert.Equal(Start.AddHours(2), activated.UpdatedAt);
    }

    [Fact]
    public async Task An_idempotent_call_does_not_advance_the_update_mark()
    {
        // The clock moves, the request succeeds, and updatedAt stays where it was
        // -- because nothing about the agent changed. Anything that stamped the
        // time unconditionally would report an edit that never happened.
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start;

        using HttpResponseMessage createdResponse =
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules.");
        AgentPayload created = await ReadAgentAsync(createdResponse);

        api.Clock.Now = Start.AddHours(1);
        (_, string activated) =
            await ActAsync(client, tenantId, created.Id, "activate", ETagOf(createdResponse));

        api.Clock.Now = Start.AddHours(9);
        (AgentPayload again, _) = await ActAsync(client, tenantId, created.Id, "activate", activated);

        Assert.Equal(Start.AddHours(1), again.UpdatedAt);
    }

    [Fact]
    public async Task Renaming_to_the_current_name_does_not_advance_the_update_mark()
    {
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start;

        using HttpResponseMessage createdResponse =
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules.");
        AgentPayload created = await ReadAgentAsync(createdResponse);

        api.Clock.Now = Start.AddHours(4);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, created.Id)}/name",
            new { name = "Sales Assistant" }, ETagOf(createdResponse));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Start, (await ReadAgentAsync(response)).UpdatedAt);
    }

    [Fact]
    public async Task An_out_of_order_request_still_applies_the_edit_but_never_moves_the_mark_backwards()
    {
        // The accepted high-water-mark rule, through routing, the handler, the
        // aggregate, EF Core and PostgreSQL. The Domain records what it is told --
        // so the new instructions are stored -- while a consumer sorting by "last
        // changed" is never told the agent changed before it did.
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start;

        using HttpResponseMessage createdResponse =
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules.");
        AgentPayload created = await ReadAgentAsync(createdResponse);

        api.Clock.Now = Start.AddHours(5);
        using HttpResponseMessage renamed = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, created.Id)}/name",
            new { name = "Renamed at hour five" }, ETagOf(createdResponse));

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        // The clock goes backwards: a delayed request, or a node whose clock is
        // behind. The version, however, is current -- this is a time problem, not
        // a concurrency one, and the two must not be confused.
        api.Clock.Now = Start.AddHours(1);

        using HttpResponseMessage late = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, created.Id)}/instructions",
            new { instructions = "Arrived out of order." }, ETagOf(renamed));

        AgentPayload updated = await ReadAgentAsync(late);

        Assert.Equal("Arrived out of order.", updated.Instructions);
        Assert.Equal(Start.AddHours(5), updated.UpdatedAt);

        // And it is the stored state, not just the response.
        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT instructions FROM ai_agents WHERE id = '{created.Id}'");

        Assert.Equal("Arrived out of order.", stored as string);
    }

    [Fact]
    public async Task Instants_reported_by_the_api_are_at_a_precision_the_database_can_store()
    {
        // DateTimeOffset counts 100ns ticks; PostgreSQL timestamptz stores
        // microseconds. The clock is wrapped so the platform never reports an
        // instant finer than it can record -- otherwise the create response and
        // the very next read would disagree about createdAt.
        using HttpClient client = api.CreateClient();
        Guid tenantId = await api.CreateTenantAsync();
        api.Clock.Now = Start.AddTicks(1237);

        AgentPayload created = await ReadAgentAsync(
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules."));

        Assert.Equal(0, created.CreatedAt.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(Start.AddTicks(1230), created.CreatedAt);
    }
}

/// <summary>
/// The AI agent API fixture with a clock the test drives.
/// </summary>
/// <remarks>
/// Replaces exactly one service -- <see cref="TimeProvider"/> -- and nothing
/// else. The endpoints, handlers, repositories, DbContext and database are the
/// real ones.
/// <para>
/// The replacement is wrapped in <see cref="MicrosecondTimeProvider"/>, the same
/// way the composition root wraps the system clock, so this fixture exercises the
/// production shape rather than a simplified one.
/// </para>
/// <para>
/// Each test sets the clock before acting. Tests within a class run sequentially,
/// so one shared clock is safe here.
/// </para>
/// </remarks>
public sealed class AiAgentTimeApiFixture : AiAgentApiFixture
{
    /// <summary>The clock the API will read. Set it before acting.</summary>
    public FixedTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new MicrosecondTimeProvider(Clock)));
}
