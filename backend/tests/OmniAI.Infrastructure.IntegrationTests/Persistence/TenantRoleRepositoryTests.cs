using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The role repository against real PostgreSQL, with every constraint in play.
/// </summary>
/// <remarks>
/// The Application tests use a fake store, so they can only prove a handler
/// surfaces a conflict it is handed. Only these can prove PostgreSQL produces
/// one, that it arrives translated into the Application's own exception type
/// rather than as a provider exception, and that the cross-tenant shapes are
/// genuinely unwritable rather than merely never written.
/// </remarks>
public sealed class TenantRoleRepositoryTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Created = new(2026, 9, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_custom_role_round_trips_with_its_permissions()
    {
        NewWorkspace workspace = await ProvisionAsync();

        TenantRole role = Custom(
            workspace,
            "Support agent",
            PlatformPermissions.AiAgentsView,
            PlatformPermissions.AiAgentsPreview);

        await using (OmniAiDbContext writing = database.CreateContext())
        {
            await new TenantRoleRepository(writing).AddAsync(role);
        }

        await using OmniAiDbContext reading = database.CreateContext();

        Versioned<TenantRole>? found = await new TenantRoleRepository(reading)
            .FindAsync(workspace.Tenant.Id, role.Id);

        Assert.NotNull(found);
        Assert.Equal("Support agent", found.Value.Name.Value);
        Assert.Equal(TenantRoleKind.Custom, found.Value.Kind);
        Assert.Equal(
            [PlatformPermissions.AiAgentsPreview, PlatformPermissions.AiAgentsView],
            found.Value.EffectivePermissions);
    }

    [Fact]
    public async Task A_role_id_is_stored_as_a_uuid()
    {
        NewWorkspace workspace = await ProvisionAsync();

        object? type = await database.ExecuteScalarAsync(
            "SELECT data_type FROM information_schema.columns "
            + "WHERE table_name = 'tenant_roles' AND column_name = 'id'");

        Assert.Equal("uuid", type as string);
        Assert.False(workspace.OwnerRole.Id.IsEmpty);
    }

    [Fact]
    public async Task A_role_belongs_to_the_tenant_it_was_created_in()
    {
        NewWorkspace first = await ProvisionAsync();
        NewWorkspace second = await ProvisionAsync();

        TenantRole role = Custom(first, "Support agent");

        await using OmniAiDbContext context = database.CreateContext();
        TenantRoleRepository repository = new(context);
        await repository.AddAsync(role);

        // Found through its own workspace, and invisible through another's -- the
        // tenant predicate is in the query, not applied to the result.
        Assert.NotNull(await repository.FindAsync(first.Tenant.Id, role.Id));
        Assert.Null(await repository.FindAsync(second.Tenant.Id, role.Id));
    }

    [Fact]
    public async Task A_duplicate_role_name_becomes_the_application_conflict()
    {
        NewWorkspace workspace = await ProvisionAsync();

        await StoreAsync(Custom(workspace, "Support agent"));

        await using OmniAiDbContext second = database.CreateContext();

        TenantRoleNameAlreadyExistsException conflict =
            await Assert.ThrowsAsync<TenantRoleNameAlreadyExistsException>(
                () => new TenantRoleRepository(second)
                    .AddAsync(Custom(workspace, "Support agent")));

        Assert.Equal("Support agent", conflict.Name);
        Assert.IsType<DbUpdateException>(conflict.InnerException);
    }

    [Fact]
    public async Task A_name_differing_only_in_case_is_refused_by_the_database()
    {
        // The one test that proves normalisation actually reaches the index. The
        // handler's pre-check is bypassed entirely here -- AddAsync is called
        // directly -- so only the unique index can reject this.
        NewWorkspace workspace = await ProvisionAsync();

        await StoreAsync(Custom(workspace, "Support agent"));

        await using OmniAiDbContext second = database.CreateContext();

        await Assert.ThrowsAsync<TenantRoleNameAlreadyExistsException>(
            () => new TenantRoleRepository(second)
                .AddAsync(Custom(workspace, "SUPPORT AGENT")));
    }

    [Fact]
    public async Task The_same_role_name_is_free_in_a_different_workspace()
    {
        // Uniqueness is per tenant and deliberately not global: two businesses
        // may each have a "Support agent", and a name one takes must not affect
        // the other.
        NewWorkspace first = await ProvisionAsync();
        NewWorkspace second = await ProvisionAsync();

        await StoreAsync(Custom(first, "Support agent"));
        await StoreAsync(Custom(second, "Support agent"));

        await using OmniAiDbContext context = database.CreateContext();

        Assert.True(await new TenantRoleRepository(context)
            .NameExistsAsync(second.Tenant.Id, "SUPPORT AGENT"));
    }

    [Fact]
    public async Task The_conflict_message_discloses_no_database_internals()
    {
        NewWorkspace workspace = await ProvisionAsync();

        await StoreAsync(Custom(workspace, "Support agent"));

        await using OmniAiDbContext second = database.CreateContext();

        TenantRoleNameAlreadyExistsException conflict =
            await Assert.ThrowsAsync<TenantRoleNameAlreadyExistsException>(
                () => new TenantRoleRepository(second)
                    .AddAsync(Custom(workspace, "Support agent")));

        foreach (string leak in
            (string[])["ux_tenant_roles", "23505", "duplicate key", "INSERT", "Npgsql", "pg_"])
        {
            Assert.DoesNotContain(leak, conflict.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_second_owner_role_is_refused_by_the_database()
    {
        // Unreachable through any published endpoint -- no caller can ask for a
        // system role -- so this proves the constraint rather than the code path.
        NewWorkspace workspace = await ProvisionAsync();

        await using OmniAiDbContext context = database.CreateContext();

        await Assert.ThrowsAsync<DomainStateException>(
            () => new TenantRoleRepository(context)
                .AddAsync(TenantRole.CreateOwner(workspace.Tenant.Id, Created)));
    }

    [Fact]
    public async Task Provisioning_creates_the_owner_role_and_assigns_it_to_the_creator()
    {
        NewWorkspace workspace = await ProvisionAsync();

        object? owners = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM tenant_roles "
            + $"WHERE tenant_id = '{workspace.Tenant.Id.Value}' AND kind = 'Owner'");

        Assert.Equal(1L, owners as long?);

        object? assignments = await database.ExecuteScalarAsync(
            "SELECT count(*) FROM tenant_role_assignments "
            + $"WHERE tenant_id = '{workspace.Tenant.Id.Value}' "
            + $"  AND user_id = '{workspace.Membership.UserId.Value}' "
            + $"  AND tenant_role_id = '{workspace.OwnerRole.Id.Value}'");

        Assert.Equal(1L, assignments as long?);
    }

    [Fact]
    public async Task The_owner_role_is_the_role_the_creator_holds()
    {
        NewWorkspace workspace = await ProvisionAsync();

        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyList<TenantRole> held = await new TenantRoleRepository(context)
            .ListAssignedRolesAsync(workspace.Membership.UserId, workspace.Tenant.Id);

        TenantRole owner = Assert.Single(held);

        Assert.True(owner.IsSystem);
        Assert.Equal(PlatformPermissions.AllCodes, owner.EffectivePermissions);
    }

    [Fact]
    public async Task An_assignment_pairing_one_tenants_membership_with_anothers_role_is_refused()
    {
        // The composite foreign keys, exercised directly. This is the shape a
        // cross-tenant privilege escalation would take, and PostgreSQL will not
        // store it -- whatever the application believes.
        NewWorkspace mine = await ProvisionAsync();
        NewWorkspace theirs = await ProvisionAsync();

        await using OmniAiDbContext context = database.CreateContext();

        context.TenantRoleAssignments.Add(TenantRoleAssignment.Create(
            mine.Membership.UserId,
            mine.Tenant.Id,
            theirs.OwnerRole.Id,
            Created));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_assignment_for_somebody_who_is_not_a_member_is_refused()
    {
        NewWorkspace workspace = await ProvisionAsync();
        UserId stranger = await TestIdentity.SeedUserAsync(database);

        await using OmniAiDbContext context = database.CreateContext();

        context.TenantRoleAssignments.Add(TenantRoleAssignment.Create(
            stranger,
            workspace.Tenant.Id,
            workspace.OwnerRole.Id,
            Created));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_role_cannot_grant_the_same_permission_twice()
    {
        NewWorkspace workspace = await ProvisionAsync();
        TenantRole role = Custom(workspace, "Support agent", PlatformPermissions.AiAgentsView);

        await StoreAsync(role);

        await using OmniAiDbContext context = database.CreateContext();

        // Written directly, bypassing the aggregate's own deduplication, so only
        // the composite primary key can refuse it.
        await Assert.ThrowsAnyAsync<Exception>(() => database.ExecuteScalarAsync(
            "INSERT INTO role_permissions (tenant_role_id, permission) "
            + $"VALUES ('{role.Id.Value}', 'ai-agents.view')"));
    }

    [Fact]
    public async Task A_role_is_renamed_when_the_expected_version_still_matches()
    {
        NewWorkspace workspace = await ProvisionAsync();
        TenantRole role = Custom(workspace, "Support agent");

        ConcurrencyToken stored = await StoreAsync(role);

        await using OmniAiDbContext context = database.CreateContext();
        TenantRoleRepository repository = new(context);

        Versioned<TenantRole> loaded =
            (await repository.FindAsync(workspace.Tenant.Id, role.Id))!;

        Assert.Equal(stored, loaded.Version);

        loaded.Value.Rename(TenantRoleName.Create("Support supervisor"), Created.AddHours(1));

        ConcurrencyToken after = await repository.UpdateAsync(loaded.Value, loaded.Version);

        Assert.NotEqual(loaded.Version, after);
    }

    [Fact]
    public async Task A_stale_version_is_refused_by_the_database()
    {
        NewWorkspace workspace = await ProvisionAsync();
        TenantRole role = Custom(workspace, "Support agent");

        ConcurrencyToken stale = await StoreAsync(role);

        // Somebody else changes the row.
        await using (OmniAiDbContext other = database.CreateContext())
        {
            TenantRoleRepository repository = new(other);
            Versioned<TenantRole> loaded =
                (await repository.FindAsync(workspace.Tenant.Id, role.Id))!;

            loaded.Value.Rename(TenantRoleName.Create("Changed elsewhere"), Created.AddHours(1));
            await repository.UpdateAsync(loaded.Value, loaded.Version);
        }

        await using OmniAiDbContext context = database.CreateContext();
        TenantRoleRepository second = new(context);

        Versioned<TenantRole> fresh =
            (await second.FindAsync(workspace.Tenant.Id, role.Id))!;

        fresh.Value.Rename(TenantRoleName.Create("From a stale screen"), Created.AddHours(2));

        await Assert.ThrowsAsync<TenantRoleVersionMismatchException>(
            () => second.UpdateAsync(fresh.Value, stale));
    }

    [Fact]
    public async Task A_permission_replacement_moves_the_version()
    {
        // Without this the parent row would be untouched when only grants change,
        // no UPDATE would be issued against tenant_roles, and two administrators
        // replacing permissions in the same window would both succeed with one of
        // them silently overwritten.
        NewWorkspace workspace = await ProvisionAsync();
        TenantRole role = Custom(workspace, "Support agent", PlatformPermissions.AiAgentsView);

        ConcurrencyToken before = await StoreAsync(role);

        await using OmniAiDbContext context = database.CreateContext();
        TenantRoleRepository repository = new(context);

        Versioned<TenantRole> loaded =
            (await repository.FindAsync(workspace.Tenant.Id, role.Id))!;

        loaded.Value.ReplacePermissions([PlatformPermissions.RolesView], Created.AddHours(1));

        ConcurrencyToken after = await repository.UpdateAsync(loaded.Value, loaded.Version);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task A_permission_replacement_is_atomic()
    {
        NewWorkspace workspace = await ProvisionAsync();

        TenantRole role = Custom(
            workspace,
            "Support agent",
            PlatformPermissions.AiAgentsView,
            PlatformPermissions.AiAgentsEdit);

        await StoreAsync(role);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            TenantRoleRepository repository = new(context);
            Versioned<TenantRole> loaded =
                (await repository.FindAsync(workspace.Tenant.Id, role.Id))!;

            loaded.Value.ReplacePermissions(
                [PlatformPermissions.RolesView, PlatformPermissions.RolesManage],
                Created.AddHours(1));

            await repository.UpdateAsync(loaded.Value, loaded.Version);
        }

        // The old set is gone and the new one is whole. A half-applied
        // replacement would show up here as a mixture of the two.
        object? stored = await database.ExecuteScalarAsync(
            "SELECT string_agg(permission, ',' ORDER BY permission) FROM role_permissions "
            + $"WHERE tenant_role_id = '{role.Id.Value}'");

        Assert.Equal("roles.manage,roles.view", stored as string);
    }

    [Fact]
    public async Task Roles_are_listed_with_owner_first_then_custom_roles_by_name()
    {
        NewWorkspace workspace = await ProvisionAsync();

        await StoreAsync(Custom(workspace, "Support agent"));
        await StoreAsync(Custom(workspace, "analyst"));

        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyList<TenantRoleSummary> listed =
            await new TenantRoleRepository(context).ListAsync(workspace.Tenant.Id);

        // Ordered on the normalised name, so capitalisation does not decide the
        // order a reader sees.
        Assert.Equal(["Owner", "analyst", "Support agent"], listed.Select(role => role.Name));
    }

    [Fact]
    public async Task The_owner_row_reports_the_catalogue_size_rather_than_a_row_count()
    {
        NewWorkspace workspace = await ProvisionAsync();

        await using OmniAiDbContext context = database.CreateContext();

        TenantRoleSummary owner = Assert.Single(
            await new TenantRoleRepository(context).ListAsync(workspace.Tenant.Id));

        Assert.Equal(PlatformPermissions.AllCodes.Count, owner.PermissionCount);
    }

    [Fact]
    public async Task Roles_from_another_workspace_are_not_listed()
    {
        NewWorkspace mine = await ProvisionAsync();
        NewWorkspace theirs = await ProvisionAsync();

        await StoreAsync(Custom(theirs, "Their role"));

        await using OmniAiDbContext context = database.CreateContext();

        IReadOnlyList<TenantRoleSummary> listed =
            await new TenantRoleRepository(context).ListAsync(mine.Tenant.Id);

        Assert.Equal(["Owner"], listed.Select(role => role.Name));
    }

    /// <summary>Provisions a whole workspace through the production path.</summary>
    private async Task<NewWorkspace> ProvisionAsync()
    {
        NewWorkspace workspace = NewWorkspace.Create(
            TenantName.Create("Acme Corporation"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]),
            await TestIdentity.SeedUserAsync(database),
            Created);

        await using OmniAiDbContext context = database.CreateContext();
        await new TenantRepository(context).AddAsync(workspace);

        return workspace;
    }

    private async Task<ConcurrencyToken> StoreAsync(TenantRole role)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new TenantRoleRepository(context).AddAsync(role);
    }

    private static TenantRole Custom(
        NewWorkspace workspace,
        string name,
        params PermissionCode[] permissions) =>
        TenantRole.CreateCustom(
            workspace.Tenant.Id,
            TenantRoleName.Create(name),
            permissions,
            Created);
}
