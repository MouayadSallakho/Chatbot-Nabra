using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The repository against real PostgreSQL, with the unique index in play.
/// </summary>
/// <remarks>
/// These are the tests that make the "the database is the authority" claim
/// checkable. The Application tests use a fake store, so they can only prove the
/// handler surfaces a conflict it is handed; only these can prove PostgreSQL
/// produces one, and that it arrives translated into the Application's own
/// exception type rather than as a provider exception.
/// </remarks>
public sealed class TenantRepositoryTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task A_tenant_is_stored_and_can_be_found_again()
    {
        NewWorkspace workspace = await NewWorkspaceAsync("Acme Corporation", NewSlug());

        await using (OmniAiDbContext writing = database.CreateContext())
        {
            await new TenantRepository(writing).AddAsync(workspace);
        }

        await using OmniAiDbContext reading = database.CreateContext();
        Tenant? found = await new TenantRepository(reading).FindByIdAsync(workspace.Tenant.Id);

        Assert.NotNull(found);
        Assert.Equal(workspace.Tenant.Id, found.Id);
        Assert.Equal("Acme Corporation", found.Name.Value);
        Assert.Equal(TenantStatus.Active, found.Status);
    }

    [Fact]
    public async Task Add_commits_so_a_separate_connection_sees_the_row()
    {
        // The contract says AddAsync commits, which is why there is no unit of
        // work. This checks the claim outside EF entirely.
        NewWorkspace workspace = await NewWorkspaceAsync("Acme Corporation", NewSlug());

        await using OmniAiDbContext context = database.CreateContext();
        await new TenantRepository(context).AddAsync(workspace);

        object? stored = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenants WHERE id = '{workspace.Tenant.Id.Value}'");

        Assert.Equal(1L, stored as long?);
    }

    [Fact]
    public async Task An_unknown_id_is_reported_as_absent_not_as_an_error()
    {
        await using OmniAiDbContext context = database.CreateContext();

        Assert.Null(await new TenantRepository(context).FindByIdAsync(TenantId.New()));
    }

    [Fact]
    public async Task Slug_existence_reflects_what_is_stored()
    {
        TenantSlug taken = TenantSlug.Create(NewSlug());
        TenantSlug free = TenantSlug.Create(NewSlug());

        await using OmniAiDbContext context = database.CreateContext();
        TenantRepository repository = new(context);

        await repository.AddAsync(await NewWorkspaceAsync("Acme", taken.Value));

        Assert.True(await repository.SlugExistsAsync(taken));
        Assert.False(await repository.SlugExistsAsync(free));
    }

    [Fact]
    public async Task A_duplicate_slug_becomes_the_application_conflict_not_a_provider_exception()
    {
        // The translation this phase exists to get right. The advisory check is
        // bypassed completely here -- AddAsync is called directly -- so the only
        // thing that can reject the second insert is the unique index.
        string slug = NewSlug();

        await StoreAsync("Acme One", slug);

        await using OmniAiDbContext second = database.CreateContext();
        NewWorkspace loser = await NewWorkspaceAsync("Acme Two", slug);

        TenantSlugAlreadyExistsException conflict =
            await Assert.ThrowsAsync<TenantSlugAlreadyExistsException>(
                () => new TenantRepository(second).AddAsync(loser));

        Assert.Equal(slug, conflict.Slug);

        // The provider exception is kept for logs, and only for logs.
        Assert.IsType<DbUpdateException>(conflict.InnerException);
    }

    [Fact]
    public async Task The_conflict_message_discloses_no_database_internals()
    {
        // What ends up in the API response body is this message, so it must not
        // name a constraint, a SQLSTATE, a table or any SQL.
        string slug = NewSlug();

        await StoreAsync("Acme One", slug);

        await using OmniAiDbContext second = database.CreateContext();
        NewWorkspace loser = await NewWorkspaceAsync("Acme Two", slug);

        TenantSlugAlreadyExistsException conflict =
            await Assert.ThrowsAsync<TenantSlugAlreadyExistsException>(
                () => new TenantRepository(second).AddAsync(loser));

        foreach (string leak in
            (string[])["ix_tenants_slug", "23505", "duplicate key", "INSERT", "tenants", "Npgsql"])
        {
            Assert.DoesNotContain(leak, conflict.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_failed_insert_leaves_no_row_and_no_tracked_leftover()
    {
        string slug = NewSlug();

        await StoreAsync("Acme One", slug);

        await using OmniAiDbContext second = database.CreateContext();
        TenantRepository repository = new(second);
        NewWorkspace loser = await NewWorkspaceAsync("Acme Two", slug);

        await Assert.ThrowsAsync<TenantSlugAlreadyExistsException>(
            () => repository.AddAsync(loser));

        // None of the four rejected rows may still be pending, or the next save
        // on this unit of work would retry inserts nobody asked for -- and each
        // retry would fail on a foreign key pointing at a tenant that was never
        // written.
        Assert.Equal(EntityState.Detached, second.Entry(loser.Tenant).State);
        Assert.Equal(EntityState.Detached, second.Entry(loser.Membership).State);
        Assert.Equal(EntityState.Detached, second.Entry(loser.OwnerRole).State);
        Assert.Equal(EntityState.Detached, second.Entry(loser.OwnerAssignment).State);

        object? rows = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

        Assert.Equal(1L, rows as long?);

        // And nothing survived for the tenant that was rolled back. These are the
        // orphans the transaction exists to prevent: a membership granting access
        // to a workspace that was never created, an Owner role for nothing, and an
        // assignment naming both.
        string rolledBack = loser.Tenant.Id.Value.ToString();

        foreach (string table in
            (string[])["tenant_memberships", "tenant_roles", "tenant_role_assignments"])
        {
            object? orphans = await database.ExecuteScalarAsync(
                $"SELECT count(*) FROM {table} WHERE tenant_id = '{rolledBack}'");

            Assert.Equal(0L, orphans as long?);
        }
    }

    [Fact]
    public async Task Concurrent_inserts_of_one_slug_produce_exactly_one_tenant()
    {
        // Eight writers, eight independent contexts and connections, one slug.
        // No lock, no serialisable transaction, no coordination in application
        // code: PostgreSQL's unique index is the only thing deciding the winner.
        const int writers = 8;
        string slug = NewSlug();

        Task<bool>[] attempts =
        [
            .. Enumerable.Range(0, writers).Select(index => AttemptAsync($"Acme {index}", slug))
        ];

        bool[] outcomes = await Task.WhenAll(attempts);

        Assert.Equal(1, outcomes.Count(succeeded => succeeded));
        Assert.Equal(writers - 1, outcomes.Count(succeeded => !succeeded));

        object? rows = await database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenants WHERE slug = '{slug}'");

        Assert.Equal(1L, rows as long?);
    }

    /// <summary>
    /// Tries one insert. True if it won, false if it was rejected as a conflict.
    /// Any other failure propagates, so a real defect cannot be counted as a
    /// tidy conflict.
    /// </summary>
    private async Task<bool> AttemptAsync(string name, string slug)
    {
        await using OmniAiDbContext context = database.CreateContext();
        NewWorkspace candidate = await NewWorkspaceAsync(name, slug);

        try
        {
            await new TenantRepository(context).AddAsync(candidate);

            return true;
        }
        catch (TenantSlugAlreadyExistsException)
        {
            return false;
        }
    }

    /// <summary>
    /// A whole workspace for a freshly seeded account, so the user foreign key is
    /// satisfied.
    /// </summary>
    /// <remarks>
    /// A new account per call rather than one shared across the class. Sharing
    /// would make the concurrent-insert test write eight memberships for one
    /// person, which is not the shape the platform actually produces.
    /// <para>
    /// It goes through <see cref="NewWorkspace.Create"/> rather than assembling
    /// the four rows here, so these tests exercise the same construction the
    /// production use case does -- including that the Owner role and the
    /// assignment are derived rather than supplied.
    /// </para>
    /// </remarks>
    private async Task<NewWorkspace> NewWorkspaceAsync(string name, string slug) =>
        NewWorkspace.Create(TenantName.Create(name), TenantSlug.Create(slug), await TestIdentity.SeedUserAsync(database), SeededPlans.Legacy, DateTimeOffset.UtcNow);

    /// <summary>Stores a workspace with a fresh owner, on its own context.</summary>
    private async Task StoreAsync(string name, string slug)
    {
        await using OmniAiDbContext context = database.CreateContext();

        await new TenantRepository(context).AddAsync(await NewWorkspaceAsync(name, slug));
    }

    /// <summary>
    /// A slug unique to one test, so tests in this class cannot collide with each
    /// other through the very constraint they are exercising.
    /// </summary>
    private static string NewSlug() => $"t{Guid.NewGuid():N}"[..20];
}
