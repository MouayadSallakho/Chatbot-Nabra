using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.AiAgents;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// Creating a workspace, and the membership that makes it usable.
/// </summary>
/// <remarks>
/// The two rows are written in one transaction, so this suite is as interested in
/// what a failed creation leaves behind as in what a successful one produces. A
/// tenant without its membership is not merely untidy: nobody could ever open it,
/// because no endpoint grants memberships and every request checks for one.
/// </remarks>
public sealed class WorkspaceProvisioningTests(AiAgentApiFixture api)
    : IClassFixture<AiAgentApiFixture>
{
    [Fact]
    public async Task Creating_a_workspace_makes_the_creator_a_member()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();
        Guid userId = await AiAgentApiFixture.CurrentUserIdAsync(client);

        Guid tenantId = await CreateWorkspaceAsync(client, "Acme Trading");

        object? memberships = await api.Database.ExecuteScalarAsync(
            "SELECT count(*) FROM tenant_memberships "
            + $"WHERE user_id = '{userId}' AND tenant_id = '{tenantId}'");

        Assert.Equal(1L, memberships as long?);
    }

    [Fact]
    public async Task The_creator_can_use_the_workspace_immediately()
    {
        // The property the membership exists for. Without it this read would be a
        // 404 against a workspace the caller had just made.
        using HttpClient client = await api.CreateSignedInClientAsync();

        Guid tenantId = await CreateWorkspaceAsync(client, "Acme Trading");

        using HttpResponseMessage read = await client.GetAsync($"/api/tenants/{tenantId}");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using HttpResponseMessage agents =
            await client.GetAsync($"/api/tenants/{tenantId}/ai-agents");

        Assert.Equal(HttpStatusCode.OK, agents.StatusCode);
    }

    [Fact]
    public async Task The_new_workspace_appears_in_the_creators_list()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();

        Guid tenantId = await CreateWorkspaceAsync(client, "Acme Trading");

        Assert.Contains(tenantId, await WorkspaceIdsAsync(client));
    }

    [Fact]
    public async Task Somebody_who_has_just_registered_owns_nothing()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();

        Assert.Empty(await WorkspaceIdsAsync(client));
    }

    [Fact]
    public async Task A_workspace_summary_carries_four_fields_and_no_membership_detail()
    {
        // It must never grow a member list, a member count, a role or an owner:
        // this is the caller's own list of workspaces, not a directory of who
        // else is in them.
        using HttpClient client = await api.CreateSignedInClientAsync();

        await CreateWorkspaceAsync(client, "Acme Trading");

        using HttpResponseMessage response = await client.GetAsync("/api/me/tenants");
        string body = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);

        JsonElement workspace = document.RootElement.EnumerateArray().Single();

        Assert.Equal(
            ["id", "name", "slug", "status"],
            workspace.EnumerateObject().Select(field => field.Name));

        foreach (string forbidden in (string[])["member", "role", "owner", "user", "permission"])
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Several_workspaces_are_listed_oldest_first()
    {
        // Deterministic, so the workspace switcher does not reshuffle itself
        // between page loads.
        using HttpClient client = await api.CreateSignedInClientAsync();

        Guid first = await CreateWorkspaceAsync(client, "First Workspace");
        Guid second = await CreateWorkspaceAsync(client, "Second Workspace");
        Guid third = await CreateWorkspaceAsync(client, "Third Workspace");

        Assert.Equal([first, second, third], await WorkspaceIdsAsync(client));

        // And the same order on a second read.
        Assert.Equal([first, second, third], await WorkspaceIdsAsync(client));
    }

    [Fact]
    public async Task A_rejected_slug_leaves_neither_a_tenant_nor_a_membership()
    {
        // The transaction, exercised through the only failure that can reach it:
        // the unique index rejecting a slug somebody already holds. Both rows must
        // roll back, or a membership would survive pointing at a tenant that was
        // never written -- and the foreign key would then have been violated too.
        using HttpClient first = await api.CreateSignedInClientAsync();
        using HttpClient second = await api.CreateSignedInClientAsync();

        Guid loser = await AiAgentApiFixture.CurrentUserIdAsync(second);
        string slug = $"c{Guid.NewGuid():N}"[..20];

        Guid winner = await CreateWorkspaceAsync(first, "Acme Trading", slug);

        using HttpResponseMessage rejected = await second.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Acme Two", slug });

        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // Exactly one tenant holds the slug: the winner's.
        object? tenants = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

        Assert.Equal(1L, tenants as long?);

        // And the loser gained no membership at all -- not to the winner's
        // workspace, and not to a tenant row that does not exist.
        object? memberships = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenant_memberships WHERE user_id = '{loser}'");

        Assert.Equal(0L, memberships as long?);

        // The loser's own list is empty, which is the same fact seen through the
        // API rather than through the database.
        Assert.Empty(await WorkspaceIdsAsync(second));

        // The winner is unaffected.
        Assert.Equal([winner], await WorkspaceIdsAsync(first));
    }

    [Fact]
    public async Task Concurrent_creations_of_one_slug_produce_exactly_one_workspace()
    {
        // Eight signed-in people racing for the same slug. The database decides,
        // and the seven losers leave no partial state: no orphan tenant, no orphan
        // membership.
        const int racers = 8;
        string slug = $"r{Guid.NewGuid():N}"[..20];

        HttpClient[] clients = await Task.WhenAll(
            Enumerable.Range(0, racers).Select(_ => api.CreateSignedInClientAsync()));

        try
        {
            HttpStatusCode[] outcomes = await Task.WhenAll(clients.Select(async client =>
            {
                using HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/tenants",
                    new { name = "Contested", slug });

                return response.StatusCode;
            }));

            Assert.Equal(1, outcomes.Count(status => status == HttpStatusCode.Created));
            Assert.Equal(racers - 1, outcomes.Count(status => status == HttpStatusCode.Conflict));

            object? tenants = await api.Database.ExecuteScalarAsync(
                $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

            Assert.Equal(1L, tenants as long?);

            // One tenant, one membership. Any more would mean a losing transaction
            // committed half of itself.
            object? memberships = await api.Database.ExecuteScalarAsync(
                "SELECT count(*) FROM tenant_memberships m "
                + $"JOIN tenants t ON t.id = m.tenant_id WHERE t.slug = '{slug}'");

            Assert.Equal(1L, memberships as long?);
        }
        finally
        {
            foreach (HttpClient client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task After_a_rejected_creation_the_same_caller_can_try_again()
    {
        // The reason the rollback is paired with detaching both entities. Without
        // it the losing membership would still be sitting in the change tracker as
        // a pending insert for a tenant that will never exist, and this next
        // attempt -- a perfectly good request -- would fail on a foreign key with
        // an error nobody could explain.
        using HttpClient first = await api.CreateSignedInClientAsync();
        using HttpClient second = await api.CreateSignedInClientAsync();

        string contested = $"c{Guid.NewGuid():N}"[..20];

        await CreateWorkspaceAsync(first, "Acme Trading", contested);

        using (HttpResponseMessage rejected = await second.PostAsJsonAsync(
            "/api/tenants",
            new { name = "Acme Two", slug = contested }))
        {
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }

        Guid recovered = await CreateWorkspaceAsync(second, "Acme Two", $"c{Guid.NewGuid():N}"[..20]);

        Assert.Equal([recovered], await WorkspaceIdsAsync(second));

        object? memberships = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenant_memberships WHERE tenant_id = '{recovered}'");

        Assert.Equal(1L, memberships as long?);
    }

    [Fact]
    public async Task Every_workspace_this_endpoint_creates_has_a_member()
    {
        // The invariant stated against the rows the endpoint itself produced. It
        // is scoped to those deliberately: the fixture also seeds deliberately
        // unowned tenants for the authorization tests, and a database-wide count
        // would be measuring those instead.
        using HttpClient client = await api.CreateSignedInClientAsync();

        Guid[] created =
        [
            await CreateWorkspaceAsync(client, "One"),
            await CreateWorkspaceAsync(client, "Two"),
            await CreateWorkspaceAsync(client, "Three"),
        ];

        string ids = string.Join(", ", created.Select(id => $"'{id}'"));

        object? orphans = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenants t WHERE t.id IN ({ids}) AND NOT EXISTS ("
            + "SELECT 1 FROM tenant_memberships m WHERE m.tenant_id = t.id)");

        Assert.Equal(0L, orphans as long?);
    }

    [Fact]
    public async Task A_workspace_cannot_be_created_for_somebody_else()
    {
        // The request body has a name and a slug and nothing else, so there is no
        // field through which a caller could name a different owner. Asserted
        // anyway: a future request record gaining one would silently become a way
        // to plant a workspace in somebody else's account.
        using HttpClient client = await api.CreateSignedInClientAsync();
        using HttpClient victim = await api.CreateSignedInClientAsync();

        Guid mine = await AiAgentApiFixture.CurrentUserIdAsync(client);
        Guid theirs = await AiAgentApiFixture.CurrentUserIdAsync(victim);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tenants",
            new
            {
                name = "Acme Trading",
                slug = $"o{Guid.NewGuid():N}"[..20],
                userId = theirs,
                ownerId = theirs,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Guid tenantId = (await ReadWorkspaceAsync(response)).Id;

        object? owner = await api.Database.ExecuteScalarAsync(
            $"SELECT user_id FROM tenant_memberships WHERE tenant_id = '{tenantId}'");

        Assert.Equal(mine, owner as Guid?);
        Assert.NotEqual(theirs, owner as Guid?);
    }

    /// <summary>Creates a workspace through the real endpoint.</summary>
    private static async Task<Guid> CreateWorkspaceAsync(
        HttpClient client,
        string name,
        string? slug = null)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/tenants",
            new { name, slug = slug ?? $"w{Guid.NewGuid():N}"[..20] });

        response.EnsureSuccessStatusCode();

        return (await ReadWorkspaceAsync(response)).Id;
    }

    private static async Task<(Guid Id, string Name)> ReadWorkspaceAsync(
        HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (
            body.RootElement.GetProperty("id").GetGuid(),
            body.RootElement.GetProperty("name").GetString() ?? string.Empty);
    }

    /// <summary>The workspace ids this client's account belongs to, in order.</summary>
    private static async Task<Guid[]> WorkspaceIdsAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/me/tenants");

        response.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return
        [
            .. body.RootElement.EnumerateArray()
                .Select(workspace => workspace.GetProperty("id").GetGuid())
        ];
    }
}
