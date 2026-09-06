using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private WebApplicationFactory<Program>? _application;

    /// <summary>Direct database access, for asserting on stored rows.</summary>
    public PostgresTestDatabase Database => _database;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();

        _application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
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

                builder.ConfigureServices(ConfigureTestServices);
            });
    }

    public async Task DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }

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

    /// <summary>Creates a tenant owned by a known account.</summary>
    public async Task<Guid> CreateTenantAsync(Guid ownerUserId, string name = "Acme Trading")
    {
        Tenant tenant = Tenant.Create(
            TenantName.Create(name),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]));

        await using OmniAiDbContext context = CreateDbContext();

        context.Tenants.Add(tenant);
        context.TenantMemberships.Add(TenantMembership.Create(
            UserId.From(ownerUserId),
            tenant.Id,
            DateTimeOffset.UtcNow));

        await context.SaveChangesAsync();

        return tenant.Id.Value;
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
