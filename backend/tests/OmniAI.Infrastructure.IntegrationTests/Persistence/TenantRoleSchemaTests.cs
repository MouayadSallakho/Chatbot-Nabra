using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// What the Phase 13 migration actually produced, read back from PostgreSQL.
/// </summary>
/// <remarks>
/// These assert against the applied schema -- <c>information_schema</c> and the
/// system catalogues -- rather than against the EF model. The model is what this
/// codebase believes; the catalogues are what a deployment will have. The two
/// disagreeing is exactly the failure worth catching, and it is what a test
/// written against the model could never see.
/// </remarks>
public sealed class TenantRoleSchemaTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Theory]
    [InlineData("tenant_roles")]
    [InlineData("role_permissions")]
    [InlineData("tenant_role_assignments")]
    public async Task The_migration_creates_the_table(string table)
    {
        object? found = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.tables "
            + $"WHERE table_schema = 'public' AND table_name = '{table}'");

        Assert.Equal(1L, found as long?);
    }

    [Fact]
    public async Task Tenant_roles_has_no_physical_xmin_column()
    {
        // xmin is PostgreSQL's own per-row transaction id system column. It
        // already exists on every table, CREATE TABLE rejects a user column of
        // that name, and the scaffolder emits one anyway -- so the generated line
        // is removed by hand after every regeneration. This is what fails if a
        // future regeneration puts it back.
        object? found = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM information_schema.columns "
            + "WHERE table_name = 'tenant_roles' AND column_name = 'xmin'");

        Assert.Equal(0L, found as long?);
    }

    [Fact]
    public async Task A_roles_name_is_unique_within_a_tenant_ignoring_case()
    {
        // The rule the whole normalised-name column exists for, asserted against
        // the index rather than against the application's pre-check -- because
        // the index is what decides when two administrators create the same name
        // at the same instant.
        object? unique = await database.ExecuteScalarAsync(
            "SELECT indisunique FROM pg_index "
            + "JOIN pg_class ON pg_class.oid = pg_index.indexrelid "
            + "WHERE pg_class.relname = 'ux_tenant_roles_tenant_name'");

        Assert.Equal(true, unique as bool?);

        object? columns = await database.ExecuteScalarAsync(
            "SELECT pg_get_indexdef(indexrelid) FROM pg_index "
            + "JOIN pg_class ON pg_class.oid = pg_index.indexrelid "
            + "WHERE pg_class.relname = 'ux_tenant_roles_tenant_name'");

        // tenant_id leads it, which is what makes the rule per workspace rather
        // than global: two businesses may each have a "Support agent" role.
        Assert.Contains("tenant_id", columns as string ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(
            "normalized_name",
            columns as string ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task At_most_one_owner_role_may_exist_per_tenant()
    {
        // A partial unique index: it constrains only the rows where kind is
        // 'Owner', so a workspace may have any number of custom roles while a
        // second Owner is refused outright. "Nothing currently creates two" is a
        // property of today's code; this is a property of the data.
        object? definition = await database.ExecuteScalarAsync(
            "SELECT pg_get_indexdef(indexrelid) FROM pg_index "
            + "JOIN pg_class ON pg_class.oid = pg_index.indexrelid "
            + "WHERE pg_class.relname = 'ux_tenant_roles_owner'");

        string sql = definition as string ?? string.Empty;

        Assert.Contains("UNIQUE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Owner", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_role_cannot_grant_the_same_permission_twice()
    {
        // The composite primary key, which makes the rule structural rather than
        // an extra constraint a later migration could drop without anything
        // obviously breaking.
        object? columns = await database.ExecuteScalarAsync(
            "SELECT string_agg(attname, ',' ORDER BY attnum) FROM pg_index "
            + "JOIN pg_class ON pg_class.oid = pg_index.indexrelid "
            + "JOIN pg_attribute ON pg_attribute.attrelid = pg_index.indrelid "
            + "  AND pg_attribute.attnum = ANY(pg_index.indkey) "
            + "WHERE pg_class.relname = 'PK_role_permissions'");

        Assert.Equal("tenant_role_id,permission", columns as string);
    }

    [Fact]
    public async Task An_assignment_must_correspond_to_a_real_membership()
    {
        // The first half of the cross-tenant guarantee, matched on BOTH columns.
        // A key on user_id alone would prove the person exists; this proves they
        // belong to the workspace the assignment names.
        object? definition = await ConstraintDefinitionAsync("fk_tenant_role_assignments_membership");

        string sql = definition as string ?? string.Empty;

        Assert.Contains("FOREIGN KEY (user_id, tenant_id)", sql, StringComparison.Ordinal);
        Assert.Contains("tenant_memberships", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_assignments_role_must_belong_to_the_same_tenant()
    {
        // The second half, and the one that closes it. It references
        // tenant_roles(id, tenant_id) rather than tenant_roles(id), so both
        // foreign keys read this row's own tenant_id column -- and PostgreSQL
        // accepts the row only when the membership and the role agree about which
        // workspace they are in.
        object? definition = await ConstraintDefinitionAsync("fk_tenant_role_assignments_role");

        string sql = definition as string ?? string.Empty;

        Assert.Contains("FOREIGN KEY (tenant_role_id, tenant_id)", sql, StringComparison.Ordinal);
        Assert.Contains("tenant_roles(id, tenant_id)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grants_are_removed_with_the_role_that_carries_them()
    {
        // Cascade, uniquely in this schema, and right here: a grant has no
        // meaning without its role and is not business content anybody would want
        // to recover.
        object? definition = await ConstraintDefinitionAsync("fk_role_permissions_role");

        Assert.Contains(
            "ON DELETE CASCADE",
            definition as string ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fk_tenant_roles_tenant")]
    [InlineData("fk_tenant_role_assignments_membership")]
    [InlineData("fk_tenant_role_assignments_role")]
    public async Task Tenant_owned_references_refuse_deletion_rather_than_cascading(string constraint)
    {
        // Restrict everywhere else, matching the rest of the schema: business
        // data is never removed as a side effect of removing something it points
        // at, and an unanswered product question stays unanswered rather than
        // being answered by accident.
        object? definition = await ConstraintDefinitionAsync(constraint);

        string sql = definition as string ?? string.Empty;

        Assert.DoesNotContain("ON DELETE CASCADE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE SET NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_role_reference_target_is_indexed()
    {
        // PostgreSQL does not index a foreign key's referencing columns
        // automatically, so without this every check of the role reference -- and
        // any future "who holds this role" -- would scan the table.
        object? found = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM pg_class WHERE relname = 'ix_tenant_role_assignments_role'");

        Assert.Equal(1L, found as long?);
    }

    [Fact]
    public async Task The_composite_key_the_assignment_references_exists()
    {
        // Not a duplicate of the primary key despite looking like one:
        // PostgreSQL requires a unique constraint on the columns a foreign key
        // references, and this pair is what forces an assignment's role and its
        // membership to name the same tenant.
        object? definition = await ConstraintDefinitionAsync("ak_tenant_roles_id_tenant");

        Assert.Contains(
            "UNIQUE (id, tenant_id)",
            definition as string ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_kind_is_stored_by_name_not_by_ordinal()
    {
        // An integer column would make the stored meaning depend on the
        // declaration order of TenantRoleKind -- so inserting a member would
        // silently reclassify every existing row, and here that means
        // reclassifying protected system roles as editable ones.
        object? type = await database.ExecuteScalarAsync(
            "SELECT data_type FROM information_schema.columns "
            + "WHERE table_name = 'tenant_roles' AND column_name = 'kind'");

        Assert.Equal("character varying", type as string);
    }

    [Fact]
    public async Task Timestamps_are_stored_with_a_time_zone()
    {
        object? types = await database.ExecuteScalarAsync(
            "SELECT string_agg(DISTINCT data_type, ',') FROM information_schema.columns "
            + "WHERE table_name IN ('tenant_roles', 'tenant_role_assignments') "
            + "  AND column_name IN ('created_at', 'updated_at')");

        Assert.Equal("timestamp with time zone", types as string);
    }

    /// <summary>The SQL PostgreSQL reports for a named constraint.</summary>
    private async Task<object?> ConstraintDefinitionAsync(string constraint) =>
        await database.ExecuteScalarAsync(
            $"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = '{constraint}'");
}
