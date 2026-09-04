using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model at design time without booting the API.
/// </summary>
/// <remarks>
/// Used only by the EF tooling. <c>migrations add</c> never opens a connection,
/// so the fallback below is a host and database name and nothing else -- no
/// user, no password, no credential of any kind belongs in source. Point
/// <c>OMNIAI_DESIGN_CONNECTION</c> at a real database when a command genuinely
/// needs one, such as <c>database update</c>.
/// </remarks>
internal sealed class OmniAiDbContextFactory : IDesignTimeDbContextFactory<OmniAiDbContext>
{
    private const string ConnectionEnvironmentVariable = "OMNIAI_DESIGN_CONNECTION";

    private const string ModelOnlyFallbackConnection = "Host=localhost;Database=omniai_design";

    public OmniAiDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? ModelOnlyFallbackConnection;

        DbContextOptions<OmniAiDbContext> options =
            new DbContextOptionsBuilder<OmniAiDbContext>()
                .UseNpgsql(connectionString)
                .Options;

        return new OmniAiDbContext(options);
    }
}
