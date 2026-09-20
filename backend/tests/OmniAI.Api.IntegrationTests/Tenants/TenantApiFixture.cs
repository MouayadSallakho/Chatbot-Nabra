using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Tenants;

/// <summary>
/// The real API, hosted in memory, talking to a real disposable PostgreSQL.
/// </summary>
/// <remarks>
/// The whole vertical slice is under test here: HTTP model binding, the Problem
/// Details pipeline, dependency injection, the handler, the repository, EF Core,
/// Npgsql and the unique index. Nothing is substituted, and in particular the EF
/// Core in-memory provider is not used -- it has no unique index, so a duplicate
/// slug would simply succeed and the most important test in this suite would pass
/// while proving nothing.
/// <para>
/// The database is provisioned by the shared <see cref="PostgresTestDatabase"/>
/// fixture, the same one the Infrastructure suite uses: a randomly named database
/// created from the real migration and dropped afterwards, located through the
/// <c>OMNIAI_TEST_POSTGRES</c> environment variable. No credential is compiled
/// in, nothing points at production, and if the variable is missing the fixture
/// fails loudly rather than quietly falling back.
/// </para>
/// <para>
/// The connection string reaches the application the same way a deployment would
/// supply it -- as configuration -- so the composition root under test is the real
/// one and no service registration is replaced.
/// </para>
/// </remarks>
public sealed class TenantApiFixture : IAsyncLifetime
{
    /// <summary>
    /// Configuration key the API reads its connection string from.
    /// </summary>
    private const string ConnectionStringSetting = "ConnectionStrings:OmniAiDatabase";

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

                // Which plan a workspace created through the API starts on. Named
                // explicitly, as a real deployment must: there is no fallback, so a
                // suite that forgot this could not create a workspace at all rather
                // than quietly creating an unlimited one.
                builder.UseSetting("Plans:DefaultPlanCode", "development");
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

    /// <summary>A client for the hosted API. Anonymous.</summary>
    public HttpClient CreateClient() =>
        (_application ?? throw new InvalidOperationException("The fixture was not initialised."))
            .CreateClient();

    /// <summary>
    /// A client with a registered, signed-in account and a valid antiforgery
    /// token.
    /// </summary>
    /// <remarks>
    /// Both tenant endpoints require authentication as of Phase 12, so almost
    /// every test here needs one. The account is created through the real
    /// registration endpoint rather than by forging a cookie, so what these tests
    /// exercise is the pipeline a browser meets.
    /// </remarks>
    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        HttpClient client = CreateClient();

        await ApiAuthentication.RegisterAsync(client);

        return client;
    }

    /// <summary>
    /// A context over the same database the API is using, for assertions that
    /// need to read what was actually persisted.
    /// </summary>
    public OmniAiDbContext CreateDbContext() => _database.CreateContext();

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
}
