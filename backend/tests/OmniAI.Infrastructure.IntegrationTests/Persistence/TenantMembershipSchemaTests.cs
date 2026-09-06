using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The membership table as PostgreSQL actually built it.
/// </summary>
/// <remarks>
/// These assert the applied schema rather than the EF model, and that matters
/// more here than anywhere else in the codebase: the user foreign key is not in
/// the EF model at all -- EF cannot relate a value-object key to Identity's
/// <c>Guid</c> one -- so it is written by hand in the migration. An assertion
/// against the model would have proved nothing about it. These read
/// <c>pg_constraint</c>.
/// </remarks>
public sealed class TenantMembershipSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task The_membership_table_exists_with_exactly_three_columns()
    {
        // Three, and no fourth. A role column, a permission set or an "is owner"
        // flag would be a schema commitment made before anybody decided what the
        // roles are; this is what would notice one being added quietly.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name || ':' || data_type || ':' || is_nullable, ', ' "
            + "ORDER BY ordinal_position) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'tenant_memberships'");

        Assert.Equal(
            "user_id:uuid:NO, tenant_id:uuid:NO, created_at:timestamp with time zone:NO",
            columns as string);
    }

    [Fact]
    public async Task The_pair_is_the_primary_key_which_is_what_makes_it_unique()
    {
        // UNIQUE(user_id, tenant_id) is structural rather than a separate
        // constraint somebody could drop: a person either belongs to a tenant or
        // does not, so a second row for the same pair has no meaning.
        object? key = await database.ExecuteScalarAsync(
            "SELECT string_agg(a.attname, ',' ORDER BY k.ordinality) "
            + "FROM pg_constraint c "
            + "JOIN pg_class t ON t.oid = c.conrelid "
            + "JOIN LATERAL unnest(c.conkey) WITH ORDINALITY AS k(attnum, ordinality) ON true "
            + "JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum "
            + "WHERE t.relname = 'tenant_memberships' AND c.contype = 'p'");

        Assert.Equal("user_id,tenant_id", key as string);
    }

    [Fact]
    public async Task A_duplicate_membership_is_refused_by_the_database()
    {
        // The constraint proven by using it, not merely by reading the catalogue.
        Guid userId = Guid.CreateVersion7();
        Guid tenantId = await SeedWorkspaceAsync(userId);

        // A data-modifying CTE: PostgreSQL does not allow INSERT in a subquery,
        // and ON CONFLICT DO NOTHING makes the refusal observable as a row count
        // rather than as an exception this test would have to catch.
        object? refused = await database.ExecuteScalarAsync(
            "WITH attempted AS ("
            + "  INSERT INTO tenant_memberships (user_id, tenant_id, created_at) "
            + $"  VALUES ('{userId}', '{tenantId}', now()) "
            + "  ON CONFLICT DO NOTHING RETURNING 1) "
            + "SELECT count(*) FROM attempted");

        Assert.Equal(0L, refused as long?);
    }

    [Theory]
    [InlineData("fk_tenant_memberships_tenant", "tenants")]
    [InlineData("fk_tenant_memberships_user", "users")]
    public async Task Both_foreign_keys_exist_and_restrict_rather_than_cascade(
        string constraint,
        string principal)
    {
        // confdeltype 'r' is RESTRICT. Not 'c': deleting an account must not
        // silently remove the memberships that make its workspaces reachable,
        // because that would strand every tenant and agent behind them, alive in
        // the database and reachable by nobody. What should happen when a
        // workspace loses its last member is an unanswered product question, and
        // RESTRICT keeps it unanswered rather than answering it by accident.
        // confdeltype is PostgreSQL's internal "char" type, which has no unique
        // concatenation operator with text -- hence the explicit cast.
        object? described = await database.ExecuteScalarAsync(
            "SELECT p.relname || ':' || c.confdeltype::text "
            + "FROM pg_constraint c "
            + "JOIN pg_class p ON p.oid = c.confrelid "
            + $"WHERE c.conname = '{constraint}' AND c.contype = 'f'");

        Assert.Equal($"{principal}:r", described as string);
    }

    [Fact]
    public async Task A_membership_naming_a_user_that_does_not_exist_is_refused()
    {
        // The hand-written foreign key doing its job. It is not in the EF model,
        // so this is the assertion that it is real.
        Guid tenantId = await SeedWorkspaceAsync(Guid.CreateVersion7());

        object? orphans = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM tenant_memberships "
            + $"WHERE tenant_id = '{tenantId}' AND user_id = '{Guid.CreateVersion7()}'");

        Assert.Equal(0L, orphans as long?);

        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteScalarAsync(
            "INSERT INTO tenant_memberships (user_id, tenant_id, created_at) "
            + $"VALUES ('{Guid.CreateVersion7()}', '{tenantId}', now())"));
    }

    [Fact]
    public async Task The_tenant_reference_is_indexed()
    {
        // PostgreSQL does not index a foreign key automatically. The user side is
        // covered by the primary key, which user_id leads; the tenant side needs
        // its own index or every check of the reference scans the table.
        object? indexes = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_indexes "
            + "WHERE tablename = 'tenant_memberships' "
            + "AND indexname = 'ix_tenant_memberships_tenant'");

        Assert.Equal(1L, indexes as long?);
    }

    [Fact]
    public async Task No_role_tables_were_created()
    {
        // Phase 12 has no roles, so the three tables Identity would create for
        // them are absent by design -- IdentityUserContext was chosen over
        // IdentityDbContext precisely to leave them out. Empty tables implying a
        // permission system that does not exist would be schema invented from a
        // template.
        object? roleTables = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.tables "
            + "WHERE table_schema = 'public' "
            + "AND table_name IN ('roles', 'user_roles', 'role_claims', "
            + "'AspNetRoles', 'AspNetUserRoles', 'AspNetRoleClaims')");

        Assert.Equal(0L, roleTables as long?);
    }

    [Fact]
    public async Task Identity_tables_follow_this_schemas_naming_rather_than_the_frameworks()
    {
        // Every other table here is snake_case. Leaving AspNetUsers in place would
        // have produced a schema where half the tables announce which framework
        // wrote them.
        object? aspNetTables = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.tables "
            + "WHERE table_schema = 'public' AND table_name LIKE 'AspNet%'");

        Assert.Equal(0L, aspNetTables as long?);

        object? expected = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.tables "
            + "WHERE table_schema = 'public' "
            + "AND table_name IN ('users', 'user_claims', 'user_logins', 'user_tokens')");

        Assert.Equal(4L, expected as long?);
    }

    [Fact]
    public async Task The_login_identifier_is_unique_in_the_database()
    {
        // RequireUniqueEmail is a UserManager option, enforced by a read followed
        // by a write -- which two concurrent registrations can both pass. The
        // index is what makes exactly one of them win.
        object? unique = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_indexes "
            + "WHERE tablename = 'users' "
            + "AND indexname = 'ix_users_normalized_email' "
            + "AND indexdef LIKE 'CREATE UNIQUE INDEX%'");

        Assert.Equal(1L, unique as long?);
    }

    /// <summary>Creates a tenant and a membership for the given account.</summary>
    private async Task<Guid> SeedWorkspaceAsync(Guid userId)
    {
        Guid tenantId = Guid.CreateVersion7();
        string slug = $"t{Guid.NewGuid():N}"[..20];

        await database.ExecuteScalarAsync(
            "INSERT INTO users (id, user_name, normalized_user_name, email, normalized_email, "
            + "email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, "
            + "access_failed_count) VALUES ("
            + $"'{userId}', 'u{userId:N}', 'U{userId:N}', 'u{userId:N}@example.test', "
            + $"'U{userId:N}@EXAMPLE.TEST', false, false, false, true, 0)");

        await database.ExecuteScalarAsync(
            "INSERT INTO tenants (id, name, slug, status) "
            + $"VALUES ('{tenantId}', 'Acme', '{slug}', 'Active')");

        await database.ExecuteScalarAsync(
            "INSERT INTO tenant_memberships (user_id, tenant_id, created_at) "
            + $"VALUES ('{userId}', '{tenantId}', now())");

        return tenantId;
    }
}
