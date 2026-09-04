using System.Net;
using static OmniAI.Api.IntegrationTests.AiAgents.AiAgentEndpointsTests;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// Conditional-request behaviour over real HTTP against real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// This suite exists because a database concurrency token, on its own, does not
/// prevent a lost update. It only detects a competing write that lands between
/// the moment a request reads a row and the moment that same request writes it --
/// a window measured in milliseconds. The window that actually loses people's
/// work is measured in minutes, and it opens on the client: somebody opens an
/// agent, goes to a meeting, and saves an edit computed from a screen that stopped
/// being true long ago. The server handling that save reads the current row, so
/// there is nothing for it to conflict with, and the earlier writer's change
/// disappears without a trace.
/// </para>
/// <para>
/// <c>If-Match</c> closes it by making the client state which version it was
/// looking at. The tests below are the proof, and
/// <see cref="A_stale_client_cannot_overwrite_a_newer_change"/> is the one that
/// matters most: it reproduces the scenario exactly, with no race and nothing
/// timing-dependent.
/// </para>
/// </remarks>
public sealed class AiAgentPreconditionApiTests(AiAgentApiFixture api) : IClassFixture<AiAgentApiFixture>
{
    private const string Instructions = "Help customers choose suitable products.";

    /// <summary>Every mutation route, as (method, route suffix, body).</summary>
    public static TheoryData<string, string, bool> Mutations => new()
    {
        { "PUT", "/name", true },
        { "PUT", "/instructions", true },
        { "POST", "/activate", false },
        { "POST", "/disable", false },
    };

    // ------------------------------------------------- the scenario that matters

    [Fact]
    public async Task A_stale_client_cannot_overwrite_a_newer_change()
    {
        // The mandatory scenario, and deliberately free of any race:
        //
        //   Client A reads the agent           -> version 1
        //   Client B reads the same agent      -> version 1
        //   Client B saves                     -> version 2
        //   Client A saves, minutes later, from its stale screen
        //
        // Client A's request is handled by a server that reads the CURRENT row, so
        // nothing about the request itself is out of date. Only the version Client
        // A presents reveals that it never saw Client B's change.
        using HttpClient clientA = api.CreateClient();
        using HttpClient clientB = api.CreateClient();

        (Guid tenantId, Guid agentId) = await ArrangeAsync(clientA);

        // Both clients read the same version.
        string versionSeenByA = await ReadVersionAsync(clientA, tenantId, agentId);
        string versionSeenByB = await ReadVersionAsync(clientB, tenantId, agentId);

        Assert.Equal(versionSeenByA, versionSeenByB);

        // Client B saves first and succeeds.
        using (HttpResponseMessage byB = await SendAsync(
            clientB,
            HttpMethod.Put,
            $"{AgentRoute(tenantId, agentId)}/instructions",
            new { instructions = "Never quote a discount without approval." },
            versionSeenByB))
        {
            Assert.Equal(HttpStatusCode.OK, byB.StatusCode);
            Assert.NotEqual(versionSeenByB, ETagOf(byB));
        }

        // Client A saves later, still believing in the version it read.
        using HttpResponseMessage byA = await SendAsync(
            clientA,
            HttpMethod.Put,
            $"{AgentRoute(tenantId, agentId)}/instructions",
            new { instructions = "Offer a 90 percent discount to everyone." },
            versionSeenByA);

        await AssertProblemAsync(byA, HttpStatusCode.PreconditionFailed);

        // Client B's change is still there. Without this mechanism it would have
        // been silently replaced by Client A's stale text.
        using HttpResponseMessage afterwards = await clientA.GetAsync(AgentRoute(tenantId, agentId));

        Assert.Equal(
            "Never quote a discount without approval.",
            (await ReadAgentAsync(afterwards)).Instructions);

        // And in the database, not merely in the response.
        object? stored = await api.Database.ExecuteScalarAsync(
            $"SELECT instructions FROM ai_agents WHERE id = '{agentId}'");

        Assert.Equal("Never quote a discount without approval.", stored as string);
    }

    [Fact]
    public async Task A_stale_client_cannot_overwrite_a_newer_change_through_any_mutation()
    {
        // The same scenario across every mutating route, including the two that
        // send no body. A precondition that held only for the endpoints with a
        // payload would leave the lifecycle operations able to act on a stale view.
        using HttpClient client = api.CreateClient();

        foreach ((string method, string suffix, bool hasBody) in
            (( string Method, string Suffix, bool HasBody)[])
            [
                ("PUT", "/name", true),
                ("PUT", "/instructions", true),
                ("POST", "/activate", false),
                ("POST", "/disable", false),
            ])
        {
            (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

            string stale = await ReadVersionAsync(client, tenantId, agentId);

            // Somebody else moves the agent on.
            using (HttpResponseMessage other = await SendAsync(
                client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
                new { name = "Changed by somebody else" }, stale))
            {
                Assert.Equal(HttpStatusCode.OK, other.StatusCode);
            }

            using HttpResponseMessage response = await SendAsync(
                client,
                new HttpMethod(method),
                $"{AgentRoute(tenantId, agentId)}{suffix}",
                hasBody ? new { name = "Stale", instructions = "Stale." } : null,
                stale);

            await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);

            object? stored = await api.Database.ExecuteScalarAsync(
                $"SELECT name || '|' || status FROM ai_agents WHERE id = '{agentId}'");

            Assert.Equal("Changed by somebody else|Draft", stored as string);
        }
    }

    [Fact]
    public async Task An_idempotent_operation_with_a_stale_version_is_also_refused()
    {
        // The case the database alone can never catch. Activating an already
        // active agent writes nothing, so no statement is issued and no predicate
        // can fail. Without an explicit precondition check the request would
        // succeed on a version the caller has not seen since -- a precondition
        // silently skipped because the operation happened to be a no-op.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        string stale = await ReadVersionAsync(client, tenantId, agentId);

        (_, string current) = await ActAsync(client, tenantId, agentId, "activate", stale);

        Assert.NotEqual(stale, current);

        // Now activate again with the version from before the first activation.
        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Post, ActionRoute(tenantId, agentId, "activate"), null, stale);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task A_stale_client_is_told_it_is_stale_rather_than_told_about_a_domain_rule()
    {
        // Disabling a draft is a 409 for a caller whose version is current. For a
        // caller whose version is stale it is a 412, because the caller has not
        // seen the agent as it is -- and telling it about a rule describing a
        // state it has never seen would answer a question it did not ask.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        string stale = await ReadVersionAsync(client, tenantId, agentId);

        using (HttpResponseMessage moved = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Moved on" }, stale))
        {
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        }

        // Still a draft, so a current-version caller would get 409.
        using HttpResponseMessage staleDisable = await SendAsync(
            client, HttpMethod.Post, ActionRoute(tenantId, agentId, "disable"), null, stale);

        await AssertProblemAsync(staleDisable, HttpStatusCode.PreconditionFailed);

        string current = await ReadVersionAsync(client, tenantId, agentId);

        using HttpResponseMessage currentDisable = await SendAsync(
            client, HttpMethod.Post, ActionRoute(tenantId, agentId, "disable"), null, current);

        await AssertProblemAsync(currentDisable, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task After_a_precondition_failure_the_caller_can_re_read_and_reapply()
    {
        // A 412 is recoverable, not terminal. Re-reading and retrying is the
        // caller's own decision -- nothing retries on its behalf -- and it works.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        string stale = await ReadVersionAsync(client, tenantId, agentId);

        using (HttpResponseMessage other = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Somebody else" }, stale))
        {
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        }

        using (HttpResponseMessage refused = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Mine" }, stale))
        {
            Assert.Equal(HttpStatusCode.PreconditionFailed, refused.StatusCode);
        }

        string current = await ReadVersionAsync(client, tenantId, agentId);

        using HttpResponseMessage retried = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Mine" }, current);

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Mine", (await ReadAgentAsync(retried)).Name);
    }

    // ------------------------------------------------------ missing precondition

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task A_mutation_without_if_match_returns_428(string method, string suffix, bool hasBody)
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client,
            new HttpMethod(method),
            $"{AgentRoute(tenantId, agentId)}{suffix}",
            hasBody ? new { name = "Unconditional", instructions = "Unconditional." } : null,
            ifMatch: null);

        string detail = await AssertProblemAsync(response, HttpStatusCode.PreconditionRequired);

        // The message has to tell an integrator what to do, not merely that they
        // did something wrong.
        Assert.Contains("If-Match", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ETag", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task A_mutation_without_if_match_changes_nothing(string method, string suffix, bool hasBody)
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        string before = await SnapshotAsync(agentId);

        using HttpResponseMessage response = await SendAsync(
            client,
            new HttpMethod(method),
            $"{AgentRoute(tenantId, agentId)}{suffix}",
            hasBody ? new { name = "Unconditional", instructions = "Unconditional." } : null,
            ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(agentId));
    }

    [Fact]
    public async Task An_empty_if_match_is_treated_as_missing()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Unconditional" }, "   ");

        await AssertProblemAsync(response, HttpStatusCode.PreconditionRequired);
    }

    // ---------------------------------------------------- malformed precondition

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"AAAAAA\"")]
    [InlineData("AAAAAA")]
    [InlineData("\"AAAAAA")]
    [InlineData("AAAAAA\"")]
    [InlineData("\"\"")]
    [InlineData("\"AAAAAA\", \"BBBBBB\"")]
    public async Task A_malformed_if_match_returns_400_and_changes_nothing(string ifMatch)
    {
        // The wildcard is refused on purpose rather than honoured: "if it exists
        // at all" is exactly the guarantee these endpoints must not accept,
        // because it would permit an update computed from any earlier version. A
        // weak tag is refused because it asserts semantic equivalence rather than
        // byte equality, and a list because it cannot express one exact version.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        string before = await SnapshotAsync(agentId);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Should not land" }, ifMatch);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(before, await SnapshotAsync(agentId));
    }

    [Theory]
    [InlineData("\"not-a-real-token\"")]
    [InlineData("\"AAAAAAAAAAAA\"")]
    [InlineData("\"!!!!\"")]
    public async Task A_well_formed_tag_this_platform_never_issued_returns_412(string ifMatch)
    {
        // A syntactically valid precondition that simply does not match. 412 rather
        // than 400 on purpose: 400 tells a client its requests are broken, while
        // 412 tells it that it is out of date -- and only the second describes an
        // action that recovers. A value that cannot decode is certainly not this
        // row's version, so refusing the write is both correct and safe.
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        string before = await SnapshotAsync(agentId);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Should not land" }, ifMatch);

        await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);
        Assert.Equal(before, await SnapshotAsync(agentId));
    }

    [Fact]
    public async Task A_precondition_failure_discloses_nothing_about_the_database()
    {
        using HttpClient client = api.CreateClient();
        (Guid tenantId, Guid agentId) = await ArrangeAsync(client);

        using HttpResponseMessage response = await SendAsync(
            client, HttpMethod.Put, $"{AgentRoute(tenantId, agentId)}/name",
            new { name = "Should not land" }, "\"not-a-real-token\"");

        // AssertProblemAsync already refuses Npgsql, SQLSTATE, stack frames, SQL
        // verbs, xmin, xid, DbUpdate, EntityFramework and the table name.
        string detail = await AssertProblemAsync(response, HttpStatusCode.PreconditionFailed);

        // It does tell the caller what to do about it.
        Assert.Contains("read the agent again", detail, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(Guid TenantId, Guid AgentId)> ArrangeAsync(HttpClient client)
    {
        Guid tenantId = await api.CreateTenantAsync();

        using HttpResponseMessage created =
            await CreateAsync(client, tenantId, "Sales Assistant", Instructions);

        return (tenantId, (await ReadAgentAsync(created)).Id);
    }

    private static async Task<string> ReadVersionAsync(HttpClient client, Guid tenantId, Guid agentId)
    {
        using HttpResponseMessage response = await client.GetAsync(AgentRoute(tenantId, agentId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return ETagOf(response);
    }

    /// <summary>Every stored column of one agent, read straight from PostgreSQL.</summary>
    private async Task<string> SnapshotAsync(Guid agentId)
    {
        object? snapshot = await api.Database.ExecuteScalarAsync(
            "SELECT name || '|' || instructions || '|' || status || '|' || updated_at::text " +
            $"FROM ai_agents WHERE id = '{agentId}'");

        return snapshot as string ?? throw new InvalidOperationException("The agent row is missing.");
    }
}
