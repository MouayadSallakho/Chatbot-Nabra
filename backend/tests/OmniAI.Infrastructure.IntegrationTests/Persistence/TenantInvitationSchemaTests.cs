using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The invitation tables as PostgreSQL actually built them.
/// </summary>
/// <remarks>
/// These read the applied schema rather than the EF model. Three of the
/// assertions here are the security of the whole invitation flow: there is no
/// column that could hold a raw token, one secret cannot unlock two invitations,
/// and an invitation's roles cannot come from another workspace.
/// </remarks>
public sealed class TenantInvitationSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Theory]
    [InlineData("tenant_invitations")]
    [InlineData("invitation_roles")]
    public async Task The_migration_creates_the_table(string table)
    {
        object? count = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.tables "
            + $"WHERE table_schema = 'public' AND table_name = '{table}'");

        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task The_token_is_stored_as_a_digest_and_there_is_no_column_for_a_raw_one()
    {
        // The property the whole scheme rests on. A copy of this table is not a
        // set of working invitation links, and there is no column a future edit
        // could quietly start putting the secret in.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name || ':' || data_type, ', ' ORDER BY ordinal_position) "
            + "FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'tenant_invitations'");

        string described = columns as string ?? string.Empty;

        Assert.Contains("token_hash:bytea", described, StringComparison.Ordinal);
        Assert.DoesNotContain("token:", described, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", described, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task One_secret_cannot_unlock_two_invitations()
    {
        object? indexes = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_indexes "
            + "WHERE tablename = 'tenant_invitations' "
            + "AND indexname = 'ux_tenant_invitations_token_hash'");

        Assert.Equal(1L, indexes);
    }

    [Fact]
    public async Task At_most_one_invitation_per_workspace_and_address_may_be_open()
    {
        // Predicated on the stored status, not on the clock: PostgreSQL will not
        // index a volatile expression, and a rule that changed as time passed
        // would not be a constraint at all.
        object? definition = await database.ExecuteScalarAsync(
            "SELECT indexdef FROM pg_indexes "
            + "WHERE tablename = 'tenant_invitations' "
            + "AND indexname = 'ux_tenant_invitations_open'");

        string index = definition as string ?? string.Empty;

        Assert.Contains("UNIQUE", index, StringComparison.Ordinal);
        Assert.Contains("tenant_id", index, StringComparison.Ordinal);
        Assert.Contains("canonical_email", index, StringComparison.Ordinal);
        Assert.Contains("Pending", index, StringComparison.Ordinal);

        // The thing that would make it unbuildable, and the reason expiry is
        // recorded rather than derived here.
        Assert.DoesNotContain("now()", index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_invitation_role_cannot_pair_two_workspaces()
    {
        // Two composite foreign keys reading the same tenant_id column: one into
        // the invitation, one into the role. A row naming an invitation here and
        // a role from elsewhere is not refused by a check -- it cannot be
        // represented.
        object? described = await database.ExecuteScalarAsync(
            "SELECT string_agg(c.conname || '->' || p.relname, ', ' ORDER BY c.conname) "
            + "FROM pg_constraint c "
            + "JOIN pg_class t ON t.oid = c.conrelid "
            + "JOIN pg_class p ON p.oid = c.confrelid "
            + "WHERE t.relname = 'invitation_roles' AND c.contype = 'f'");

        Assert.Equal(
            "fk_invitation_roles_invitation_tenant->tenant_invitations, "
            + "fk_invitation_roles_role->tenant_roles",
            described as string);
    }

    [Fact]
    public async Task Both_invitation_role_references_include_the_tenant()
    {
        // The assertion that the columns above are genuinely composite. A pair of
        // single-column keys would satisfy the previous test and protect nothing.
        object? described = await database.ExecuteScalarAsync(
            "SELECT string_agg(c.conname || ':' || cardinality(c.conkey)::text, ', ' "
            + "ORDER BY c.conname) "
            + "FROM pg_constraint c "
            + "JOIN pg_class t ON t.oid = c.conrelid "
            + "WHERE t.relname = 'invitation_roles' AND c.contype = 'f'");

        Assert.Equal(
            "fk_invitation_roles_invitation_tenant:2, fk_invitation_roles_role:2",
            described as string);
    }

    [Fact]
    public async Task An_invitation_cannot_name_the_same_role_twice()
    {
        // Structural rather than checked: the pair is the primary key.
        object? key = await database.ExecuteScalarAsync(
            "SELECT string_agg(a.attname, ',' ORDER BY k.ordinality) "
            + "FROM pg_constraint c "
            + "JOIN pg_class t ON t.oid = c.conrelid "
            + "JOIN LATERAL unnest(c.conkey) WITH ORDINALITY AS k(attnum, ordinality) ON true "
            + "JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum "
            + "WHERE t.relname = 'invitation_roles' AND c.contype = 'p'");

        Assert.Equal("invitation_id,tenant_role_id", key as string);
    }

    [Fact]
    public async Task Roles_go_with_the_invitation_that_carries_them()
    {
        // 'c' is CASCADE. A grant only means anything as part of an invitation,
        // so it cannot outlive one.
        object? described = await database.ExecuteScalarAsync(
            "SELECT c.confdeltype::text FROM pg_constraint c "
            + "WHERE c.conname = 'fk_invitation_roles_invitation_tenant'");

        Assert.Equal("c", described as string);
    }

    [Fact]
    public async Task A_role_named_by_an_invitation_cannot_be_deleted_out_from_under_it()
    {
        // 'r' is RESTRICT. Role deletion is not implemented; when it is, whoever
        // adds it has to decide what happens to invitations naming that role
        // rather than silently emptying them.
        object? described = await database.ExecuteScalarAsync(
            "SELECT c.confdeltype::text FROM pg_constraint c "
            + "WHERE c.conname = 'fk_invitation_roles_role'");

        Assert.Equal("r", described as string);
    }

    [Fact]
    public async Task The_status_is_stored_by_name_not_by_ordinal()
    {
        // Reordering the enum must not silently change what every stored row
        // means -- and the open-invitation index is predicated on the text.
        object? type = await database.ExecuteScalarAsync(
            "SELECT data_type FROM information_schema.columns "
            + "WHERE table_name = 'tenant_invitations' AND column_name = 'status'");

        Assert.Equal("character varying", type as string);
    }

    [Fact]
    public async Task Timestamps_are_stored_with_a_time_zone()
    {
        object? described = await database.ExecuteScalarAsync(
            "SELECT string_agg(column_name || ':' || data_type, ', ' ORDER BY column_name) "
            + "FROM information_schema.columns "
            + "WHERE table_name = 'tenant_invitations' "
            + "AND column_name IN ('created_at', 'expires_at', 'accepted_at', 'revoked_at')");

        Assert.Equal(
            "accepted_at:timestamp with time zone, created_at:timestamp with time zone, "
            + "expires_at:timestamp with time zone, revoked_at:timestamp with time zone",
            described as string);
    }

    [Fact]
    public async Task Tenant_invitations_has_no_physical_xmin_column()
    {
        // Nothing on this table is conditionally written, so it maps no
        // concurrency token -- but the guard is worth having, because a
        // regenerated migration would add a real column if one were ever mapped.
        object? count = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_schema = 'public' AND table_name = 'tenant_invitations' "
            + "AND column_name = 'xmin'");

        Assert.Equal(0L, count);
    }
}
