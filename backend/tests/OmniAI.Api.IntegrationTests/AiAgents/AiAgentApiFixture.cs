using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.AiAgents;

/// <summary>
/// The real API, hosted in memory, talking to a real disposable PostgreSQL.
/// </summary>
/// <remarks>
/// The whole vertical slice is under test: routing, model binding, the Problem
/// Details pipeline, dependency injection, the handlers, the repository, EF Core,
/// Npgsql, the foreign key and the <c>xmin</c> concurrency token. Nothing is
/// substituted, and in particular the EF Core in-memory provider is not used --
/// it has no foreign keys and no <c>xmin</c>, so the isolation and concurrency
/// tests would pass against it while proving nothing.
/// <para>
/// The database is provisioned by the shared <see cref="PostgresTestDatabase"/>
/// fixture, the same one the Infrastructure suite uses: a randomly named database
/// created from the real migrations and dropped afterwards, located through the
/// <c>OMNIAI_TEST_POSTGRES</c> environment variable. No credential is compiled
/// in, nothing points at production, and if the variable is missing the fixture
/// fails loudly rather than quietly falling back.
/// </para>
/// <para>
/// The connection string reaches the application the same way a deployment would
/// supply it -- as configuration -- so the composition root under test is the
/// real one.
/// </para>
/// <para>
/// <strong>A real Redis is configured too, from Phase 16.</strong> Public chat
/// writes fail closed without a reachable protection store, so a host with none
/// would answer 503 to every hosted-chat test for a reason that has nothing to do
/// with what they assert. Each fixture instance gets its own key namespace and its
/// own generated network-key secret, so classes running in parallel never share
/// counters and nothing secret-shaped is committed.
/// </para>
/// </remarks>
public class AiAgentApiFixture : IAsyncLifetime
{
    /// <summary>Configuration key the API reads its connection string from.</summary>
    private const string ConnectionStringSetting = "ConnectionStrings:OmniAiDatabase";

    /// <summary>Configuration key the API reads its AI provider credential from.</summary>
    /// <remarks>
    /// Named here so it can be blanked. No test ever reads or supplies a real
    /// value for it.
    /// </remarks>
    private const string ApiKeySetting = "OPENAI_API_KEY";

    private readonly PostgresTestDatabase _database = new();
    private readonly RedisTestServer _redis = new();

    private WebApplicationFactory<Program>? _application;

    /// <summary>Direct database access, for asserting on stored rows.</summary>
    public PostgresTestDatabase Database => _database;

    /// <summary>
    /// The protection store this host counts in, for the assertions that read it.
    /// </summary>
    /// <summary>Where the default plan for new workspaces is configured.</summary>
    private const string DefaultPlanSetting = "Plans:DefaultPlanCode";

    public RedisTestServer Redis => _redis;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        _application = new WebApplicationFactory<Program>().WithWebHostBuilder(ConfigureHost);
    }

    /// <summary>
    /// The configuration every instance of this host runs with.
    /// </summary>
    /// <remarks>
    /// One method rather than a lambda, so a second instance of the same
    /// application (see <see cref="CreateSecondInstance"/>) is configured
    /// identically and cannot drift from the first.
    /// </remarks>
    private void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseSetting(ConnectionStringSetting, _database.ConnectionString);

        // No AI provider, whatever the developer's machine happens to have
        // configured.
        //
        // The hosted application reads OPENAI_API_KEY through
        // configuration, and configuration includes the process
        // environment -- so without this line a machine with a real key
        // would silently register the real OpenAI adapter and these tests
        // would make live, billed calls to the internet. That is not a
        // hypothetical: it happened once during development, which is why
        // the override is here rather than in a comment asking people to
        // remember.
        //
        // Overriding it to blank makes every suite built on this fixture
        // deterministic and offline. The suite that needs the real adapter
        // installs it explicitly over a canned socket.
        if (DisableAiProvider)
        {
            builder.UseSetting(ApiKeySetting, string.Empty);
        }

        // Public chat protection, configured exactly as a deployment would
        // configure it: a connection string, a secret from the
        // environment, and a non-secret policy section. Nothing here
        // replaces a service, so what the suite exercises is the real
        // Redis-backed limiter and the real HMAC key derivation.
        // Which plan a workspace created through the API starts on. Named
        // explicitly, as a real deployment must: there is no fallback, so
        // a suite that forgot this would fail to create a workspace rather
        // than quietly create an unlimited one.
        builder.UseSetting(DefaultPlanSetting, SeededPlans.DevelopmentCode);

        builder.UseSetting(RedisConnectionSetting, _redis.ConnectionString);
        builder.UseSetting(NetworkKeySecretSetting, _redis.NetworkKeySecret);
        builder.UseSetting(KeyPrefixSetting, _redis.KeyPrefix);

        // Phase 37.3: the account limiter's counters, in this fixture's namespace,
        // so classes sharing one Redis never share a sign-in budget.
        builder.UseSetting("AccountProtection:KeyPrefix", _redis.KeyPrefix + ":acp");

        foreach ((string key, string value) in HostSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            // Gives these in-memory requests a connection to have come
            // from. TestServer has no socket, so without this every public
            // chat write is refused for having no establishable origin --
            // which is the correct production behaviour and useless for a
            // suite asserting on anything else. See SimulatedPeerStartupFilter.
            services.AddSingleton<IStartupFilter>(
                new SimulatedPeerStartupFilter(
                    SimulatedPeerStartupFilter.Default,
                    honourHeader: true));

            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// A second, independent instance of the same application.
    /// </summary>
    /// <remarks>
    /// Same database, same Redis, same configuration; its own process state,
    /// hubs and connections. Two of these are two API instances behind a load
    /// balancer as far as SignalR is concerned, which is what the backplane
    /// tests need. The caller owns and disposes it.
    /// </remarks>
    public WebApplicationFactory<Program> CreateSecondInstance() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(ConfigureHost);

    /// <summary>The first instance of the hosted application.</summary>
    public WebApplicationFactory<Program> Application =>
        _application ?? throw new InvalidOperationException("The fixture was not initialised.");

    public async Task DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }

        // Only this fixture's own keys, never a FLUSHDB: other test classes are
        // running in parallel against the same server.
        await _redis.ClearAsync();
        await _database.DisposeAsync();
    }

    /// <summary>
    /// The hosted application's service provider.
    /// </summary>
    /// <remarks>
    /// For the few assertions that are about the composition root itself rather
    /// than about a request -- notably that the AI text generator a real
    /// deployment resolves is the one that refuses, not a test double.
    /// </remarks>
    public IServiceProvider Services =>
        (_application ?? throw new InvalidOperationException("The fixture was not initialised."))
            .Services;

    /// <summary>A client for the hosted API.</summary>
    public HttpClient CreateClient() =>
        (_application ?? throw new InvalidOperationException("The fixture was not initialised."))
            .CreateClient();

    /// <summary>
    /// A context over the same database the API is using, for assertions that
    /// need to read what was actually persisted.
    /// </summary>
    public OmniAiDbContext CreateDbContext() => _database.CreateContext();

    /// <summary>
    /// A browser with no OmniAI account, as a visitor's is.
    /// </summary>
    /// <param name="peer">
    /// The network this visitor appears to be on. Defaults to the host's own, and
    /// is given explicitly by the tests that need two genuinely different networks
    /// — a per-network limit cannot be shown to partition anything with only one.
    /// See <see cref="SimulatedPeerStartupFilter"/> for why the connection has to
    /// be simulated at all.
    /// </param>
    /// <remarks>
    /// Cookies are kept between requests, which is the whole point: the
    /// conversation credential is issued as one and has to travel back by itself.
    /// The antiforgery pair is established first, exactly as the hosted page does.
    /// <para>
    /// Lives on the base fixture rather than the website-chat one because three
    /// suites now need an anonymous browser, and a second copy of "establish the
    /// antiforgery pair, keep the cookies" is exactly the duplicate that drifts.
    /// </para>
    /// </remarks>
    public async Task<HttpClient> CreateVisitorAsync(IPAddress? peer = null)
    {
        HttpClient client = CreateClient();

        if (peer is not null)
        {
            client.DefaultRequestHeaders.Add(
                SimulatedPeerStartupFilter.HeaderName,
                peer.ToString());
        }

        // Anonymous, and still antiforgery-checked. Being unauthenticated is not
        // a reason to skip it: there is a session here, and it is precisely what a
        // cross-site request would ride on.
        using HttpResponseMessage csrf =
            await client.GetAsync(new Uri("/api/auth/csrf", UriKind.Relative));

        if (csrf.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException(
                $"The antiforgery bootstrap answered {csrf.StatusCode}.");
        }

        if (WebChat.WebChatApiFixture.ReadCookie(csrf, "XSRF-TOKEN") is string token)
        {
            client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        }

        return client;
    }

    /// <summary>
    /// A client with a registered, signed-in account and a valid antiforgery
    /// token.
    /// </summary>
    /// <remarks>
    /// Every suite built on this fixture needs one, because every endpoint under
    /// test now requires authentication. The account is created through the real
    /// registration endpoint; nothing here forges a cookie or installs a fake
    /// principal, so what these tests exercise is the pipeline a browser meets.
    /// </remarks>
    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        HttpClient client = CreateClient();

        await ApiAuthentication.RegisterAsync(client);

        return client;
    }

    /// <summary>
    /// Creates a tenant owned by this client's account and returns its id.
    /// </summary>
    /// <remarks>
    /// Seeded through the Domain factory rather than through the tenant endpoint.
    /// These tests are about AI agents, and routing every fixture through a second
    /// slice would make an unrelated change to tenant onboarding able to fail
    /// them. The rows it produces are identical either way.
    /// <para>
    /// <strong>The membership is seeded with it, and that is not incidental.</strong>
    /// A tenant without one is unreachable: every request for it answers 404, so
    /// a fixture that wrote only the tenant row would make every test in this
    /// project fail for a reason that has nothing to do with what it asserts.
    /// </para>
    /// </remarks>
    public async Task<Guid> CreateTenantAsync(HttpClient owner, string name = "Acme Trading")
    {
        ArgumentNullException.ThrowIfNull(owner);

        return await CreateTenantAsync(await CurrentUserIdAsync(owner), name);
    }

    /// <summary>Creates a workspace owned by a known account.</summary>
    /// <remarks>
    /// <strong>The Owner role and its assignment are seeded too, and that is not
    /// incidental.</strong> From Phase 13 a membership grants nothing on its own:
    /// what may be done inside a workspace comes from the roles the member holds.
    /// A fixture that wrote only the tenant and the membership would make every
    /// test in this project fail with 403 for a reason that has nothing to do with
    /// what it asserts.
    /// <para>
    /// It goes through <see cref="NewWorkspace.Create"/> rather than assembling
    /// the four rows here, so the fixture cannot drift from what production
    /// provisioning actually writes.
    /// </para>
    /// </remarks>
    public async Task<Guid> CreateTenantAsync(Guid ownerUserId, string name = "Acme Trading")
    {
        NewWorkspace workspace = NewWorkspace.Create(
            TenantName.Create(name),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]),
            UserId.From(ownerUserId),
            SeededPlans.Legacy,
            DateTimeOffset.UtcNow);

        await using OmniAiDbContext context = CreateDbContext();

        context.Tenants.Add(workspace.Tenant);
        context.TenantMemberships.Add(workspace.Membership);
        context.TenantRoles.Add(workspace.OwnerRole);
        context.TenantRoleAssignments.Add(workspace.OwnerAssignment);

        // The normalised name is a shadow property the role repository maintains;
        // this fixture writes rows directly, so it sets it the same way.
        context.Entry(workspace.OwnerRole)
            .Property<string>("NormalisedName")
            .CurrentValue = workspace.OwnerRole.Name.Normalised;

        await context.SaveChangesAsync();

        return workspace.Tenant.Id.Value;
    }

    /// <summary>
    /// Creates a tenant nobody is a member of.
    /// </summary>
    /// <remarks>
    /// For the tests that need a tenant which genuinely exists and which the
    /// caller genuinely cannot reach. Without this the only unreachable tenant a
    /// test could name is an invented id, which proves less: it would leave open
    /// the possibility that the API answers differently for real ids.
    /// </remarks>
    public async Task<Guid> CreateUnownedTenantAsync(string name = "Rival Trading")
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create(name),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = CreateDbContext();

        // Deliberately just the tenant: no membership, no role, no assignment.
        // The property under test is that a workspace which genuinely exists is
        // unreachable, and the cleanest way to arrange that is a workspace nobody
        // belongs to. Seeding a second account and giving it the workspace would
        // test the same refusal through more machinery.
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        return tenant.Id.Value;
    }

    /// <summary>
    /// A second client signed in as the same account.
    /// </summary>
    /// <remarks>
    /// For the concurrency scenarios, which are one person with the application
    /// open in two tabs rather than two different people. Two separate accounts
    /// would test something else entirely -- and, since neither would be a member
    /// of the other's workspace, would produce 404s instead of the precondition
    /// failures those tests exist to prove.
    /// <para>
    /// It signs in rather than registering, so the two clients share an account
    /// and differ only in their cookie jar, which is exactly how two tabs differ.
    /// </para>
    /// </remarks>
    public async Task<HttpClient> CreateSecondClientForAsync(HttpClient existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        string email = await CurrentEmailAsync(existing);

        HttpClient client = CreateClient();

        using HttpResponseMessage signedIn =
            await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password);

        signedIn.EnsureSuccessStatusCode();

        return client;
    }

    /// <summary>The address this client is signed in as.</summary>
    public static async Task<string> CurrentEmailAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        response.EnsureSuccessStatusCode();

        using JsonDocument body =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("email").GetString() ?? string.Empty;
    }

    /// <summary>The account this client is signed in as.</summary>
    public static async Task<Guid> CurrentUserIdAsync(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        response.EnsureSuccessStatusCode();

        using JsonDocument body =
            JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Confirms the hosted application really resolved the test database rather
    /// than silently starting with a blank connection string.
    /// </summary>
    public string ResolvedConnectionString()
    {
        WebApplicationFactory<Program> application =
            _application ?? throw new InvalidOperationException("The fixture was not initialised.");

        using IServiceScope scope = application.Services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<OmniAiDbContext>()
            .Database.GetConnectionString() ?? string.Empty;
    }

    /// <summary>
    /// Whether to force this host to have no AI provider.
    /// </summary>
    /// <remarks>
    /// True everywhere except the explicitly gated live smoke fixture, which is
    /// the only place a real credential is allowed to reach a hosted application.
    /// Keeping the default on this side means a new fixture is offline unless
    /// somebody deliberately says otherwise.
    /// </remarks>
    protected virtual bool DisableAiProvider => true;

    /// <summary>Configuration key naming the protection store.</summary>
    private const string RedisConnectionSetting = "ConnectionStrings:OmniAiProtectionRedis";

    /// <summary>Configuration key holding the network-key secret.</summary>
    private const string NetworkKeySecretSetting = "PUBLIC_CHAT_NETWORK_KEY_SECRET";

    /// <summary>Configuration key naming this host's counter namespace.</summary>
    private const string KeyPrefixSetting = "PublicChatProtection:KeyPrefix";

    /// <summary>
    /// Extra configuration this fixture wants the host to run with.
    /// </summary>
    /// <remarks>
    /// <strong>Configuration, never a code path.</strong> The suites that need to
    /// see a rate limit fire set a deliberately tiny one here, and the suites about
    /// proxy trust name their trusted proxies here -- both exactly as an operator
    /// would. Nothing in the application knows it is being tested: there is no
    /// <c>IsDevelopment</c> branch, no test-only endpoint and no
    /// way to switch the protection off — because a bypass that exists for tests
    /// is a bypass that exists.
    /// </remarks>
    protected virtual IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Hook for the fixtures that need one service replaced.
    /// </summary>
    /// <remarks>
    /// Empty here on purpose: this fixture substitutes nothing, so the endpoints,
    /// handlers, repositories and the real system clock are exactly what a
    /// deployment would run. Only the time and concurrency fixtures override it,
    /// and each documents the single service it replaces and why.
    /// </remarks>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }
}
