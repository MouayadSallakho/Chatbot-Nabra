using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Identity;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Seeds accounts for tests that need a membership's foreign key satisfied.
/// </summary>
/// <remarks>
/// <strong>No password is set, deliberately.</strong> These tests are about
/// tenants and memberships, not credentials: they need a row in <c>users</c> that
/// the foreign key accepts, and nothing more. Hashing a password here would make
/// every one of them pay for a deliberately slow key derivation to prove
/// something none of them asserts. The credential behaviour has its own suite,
/// which goes through <c>UserManager</c> rather than writing rows directly.
/// </remarks>
internal static class TestIdentity
{
    /// <summary>Inserts an account and returns its identity.</summary>
    public static async Task<UserId> SeedUserAsync(PostgresTestDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        UserId id = UserId.New();
        string address = $"user-{id.Value:N}@example.test";

        await using OmniAiDbContext context = database.CreateContext();

        // The normalised columns are set by hand because UserManager is not
        // involved. They carry the unique indexes, so leaving them null would make
        // every seeded account collide with every other one.
        context.Set<OmniAiUser>().Add(new OmniAiUser
        {
            Id = id.Value,
            UserName = address,
            NormalizedUserName = address.ToUpperInvariant(),
            Email = address,
            NormalizedEmail = address.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        });

        await context.SaveChangesAsync();

        return id;
    }
}
