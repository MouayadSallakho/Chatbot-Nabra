using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Infrastructure.Persistence;
using static OmniAI.Api.IntegrationTests.AiAgents.AiAgentEndpointsTests;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// The database race, over real HTTP against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// The second of the two concurrency cases, and the narrower one. Here the
/// caller's <c>If-Match</c> is <strong>current</strong> when the request begins
/// -- it has seen the agent exactly as it is -- and another transaction commits
/// in the milliseconds before this request writes. The precondition was true when
/// it was checked and false by the time it mattered.
/// </para>
/// <para>
/// It is answered <c>412</c>, the same as a stale client, because from the
/// caller's point of view the situation is identical: the version it presented is
/// no longer the resource's version, and the fix is to read again. What must
/// never happen is the write landing anyway.
/// </para>
/// <para>
/// The companion suite <see cref="AiAgentPreconditionApiTests"/> covers the case
/// that actually loses people's work -- client think time -- and needs no race at
/// all.
/// </para>
/// </remarks>
public sealed class AiAgentConcurrencyApiTests(AiAgentConcurrencyApiFixture api)
    : IClassFixture<AiAgentConcurrencyApiFixture>
{
    [Fact]
    public async Task A_race_during_a_rename_is_reported_as_a_precondition_failure()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        // Somebody else changes the row in the window between this request reading
        // it and writing it back. The caller's ETag was current when it was sent.
        api.ArmCompetingWrite(
            $"UPDATE ai_agents SET name = 'Renamed by someone else' WHERE id = '{agentId}'");

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Renamed by the racing request" }, version);

        Assert.True(api.CompetingWriteFired, "The competing write never ran, so nothing was raced.");

        string detail = await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);

        // The other writer's value survived. This is the whole point: a lost
        // update would have silently discarded their change.
        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Renamed by someone else", stored as string);
        Assert.DoesNotContain("Renamed by the racing request", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_race_during_an_instructions_update_leaves_the_other_text_in_place()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        api.ArmCompetingWrite(
            $"UPDATE ai_agents SET instructions = 'Never quote a discount.' WHERE id = '{agentId}'");

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/instructions",
            new { instructions = "Offer a 90 percent discount." }, version);

        Assert.True(api.CompetingWriteFired, "The competing write never ran, so nothing was raced.");
        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT instructions FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Never quote a discount.", stored as string);
    }

    [Fact]
    public async Task A_race_during_a_lifecycle_change_is_reported_as_a_precondition_failure()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        api.ArmCompetingWrite(
            $"UPDATE ai_agents SET name = 'Renamed by someone else' WHERE id = '{agentId}'");

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Post, ActionRoute(tenantId, agentId, "activate"), null, version);

        Assert.True(api.CompetingWriteFired, "The competing write never ran, so nothing was raced.");
        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);

        // The activation did not land either.
        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT status FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Draft", stored as string);
    }

    [Fact]
    public async Task Nothing_is_retried_automatically()
    {
        // The deliberate absence of a retry. Replaying the caller's intent against
        // state they have not seen would overwrite the other writer's change,
        // which is the exact loss this mechanism exists to prevent -- so a
        // precondition failure is reported, once, and the caller decides.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        api.ArmCompetingWrite($"UPDATE ai_agents SET name = 'Someone else' WHERE id = '{agentId}'");

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Stale" }, version);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);

        // Had anything retried, the second attempt would have read the fresh row,
        // succeeded, and left "Stale" behind. The interceptor is disarmed after
        // firing, so a retry would not have been blocked.
        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Someone else", stored as string);
    }

    [Fact]
    public async Task After_a_race_the_caller_can_read_and_reapply_successfully()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        api.ArmCompetingWrite($"UPDATE ai_agents SET name = 'Someone else' WHERE id = '{agentId}'");

        using (HttpResponseMessage refused = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Stale" }, version))
        {
            Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        }

        using HttpResponseMessage reread = await client.GetAsync(AgentRoute(tenantId, agentId));
        Assert.Equal("Someone else", (await ReadAgentAsync(reread)).Name);

        using HttpResponseMessage retried = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Reapplied" }, ETagOf(reread));

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Reapplied", (await ReadAgentAsync(retried)).Name);
    }

    [Fact]
    public async Task Parallel_conditional_renames_never_produce_a_server_error()
    {
        // The unforced version. Every request presents the same current version,
        // so at most one can win -- and whether any two actually overlap is up to
        // the scheduler. Nothing here asserts that a race occurred; what it asserts
        // is that every outcome is one the contract defines, that no request
        // produced a 500, that exactly one succeeded, and that the row is left
        // holding that winner's value rather than a mixture.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId, string version) = await ArrangeAsync(client);

        string[] names = [.. Enumerable.Range(1, 8).Select(index => $"Writer {index}")];

        HttpResponseMessage[] responses = await Task.WhenAll(names.Select(name => SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name }, version)));

        string? winner;

        try
        {
            foreach (HttpResponseMessage response in responses)
            {
                Assert.True(
                    response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PreconditionFailed,
                    $"Unexpected status {(int)response.StatusCode} from a parallel rename.");
            }

            HttpResponseMessage[] succeeded =
                [.. responses.Where(response => response.StatusCode == HttpStatusCode.OK)];

            // All eight presented the same version, so exactly one can have been
            // applied. Two successes would mean one caller's change was lost.
            HttpResponseMessage single = Assert.Single(succeeded);
            winner = (await ReadAgentAsync(single)).Name;
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT name FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal(winner, stored as string);
        Assert.Contains(stored as string, names);
    }

    private async Task<(Guid TenantId, Guid AgentId, string ETag)> ArrangeAsync(HttpClient client)
    {
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage created =
            await CreateAsync(client, tenantId, "Sales Assistant", "Original rules.");

        return (tenantId, (await ReadAgentAsync(created)).Id, ETagOf(created));
    }
}

/// <summary>
/// The AI agent API fixture with one competing writer wired in.
/// </summary>
/// <remarks>
/// Everything the base fixture provides, plus a single
/// <see cref="CompetingWriterInterceptor"/> registered as an EF Core interceptor.
/// It is inert until a test arms it, so the application under test behaves
/// exactly as it normally would until the moment a race is wanted.
/// <para>
/// No production service is replaced -- the DbContext registration, the
/// repositories, the handlers and the endpoints are the real ones. The
/// interceptor is additive, which is why this stays an honest end-to-end test
/// rather than a simulation.
/// </para>
/// </remarks>
public sealed class AiAgentConcurrencyApiFixture : AiAgentApiFixture
{
    private readonly CompetingWriterInterceptor _competingWriter = new();

    /// <summary>Whether the armed competing write actually ran.</summary>
    public bool CompetingWriteFired => _competingWriter.Fired;

    /// <summary>
    /// Arms one out-of-band statement to run inside the next save, on its own
    /// connection to the same test database.
    /// </summary>
    public void ArmCompetingWrite(string sql) =>
        _competingWriter.Arm(Database.ConnectionString, sql);

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<IDbContextOptionsConfiguration<OmniAiDbContext>>(
            new AddCompetingWriter(_competingWriter));

    /// <summary>
    /// Appends the interceptor to the options the application already built.
    /// </summary>
    /// <remarks>
    /// <c>IDbContextOptionsConfiguration</c> is EF Core's supported way to add to
    /// an existing <c>AddDbContext</c> registration. It is used here in preference
    /// to re-registering the context, which would mean this suite testing a
    /// DbContext configured by the test rather than the one the composition root
    /// configures.
    /// </remarks>
    private sealed class AddCompetingWriter(IInterceptor interceptor)
        : IDbContextOptionsConfiguration<OmniAiDbContext>
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.AddInterceptors(interceptor);
    }
}
