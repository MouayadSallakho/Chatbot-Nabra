using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Authorization;

/// <summary>
/// The aggregate that decides what holding a role permits.
/// </summary>
public sealed class TenantRoleTests
{
    private static readonly TenantId Workspace = TenantId.New();
    private static readonly DateTimeOffset Created = new(2026, 9, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(3);

    [Fact]
    public void An_owner_role_is_a_protected_system_role()
    {
        TenantRole owner = TenantRole.CreateOwner(Workspace, Created);

        Assert.Equal(TenantRoleKind.Owner, owner.Kind);
        Assert.True(owner.IsSystem);
        Assert.Equal("Owner", owner.Name.Value);
        Assert.Equal(Workspace, owner.TenantId);
    }

    [Fact]
    public void An_owner_grants_every_permission_the_platform_defines()
    {
        // The central property of this design, and it is asserted against the
        // catalogue rather than a hard-coded list -- so a capability added in a
        // future release is covered by this test the moment it is added, without
        // anybody remembering to extend it.
        TenantRole owner = TenantRole.CreateOwner(Workspace, Created);

        Assert.Equal(PlatformPermissions.AllCodes, owner.EffectivePermissions);
    }

    [Fact]
    public void An_owner_stores_no_permission_rows()
    {
        // This is what makes the property above structural rather than
        // maintained. There is nothing to backfill when the catalogue grows,
        // because an Owner's authority was never written down.
        TenantRole owner = TenantRole.CreateOwner(Workspace, Created);

        Assert.Empty(owner.Grants);
        Assert.NotEmpty(owner.EffectivePermissions);
    }

    [Fact]
    public void An_owner_cannot_be_renamed()
    {
        TenantRole owner = TenantRole.CreateOwner(Workspace, Created);

        DomainStateException refused = Assert.Throws<DomainStateException>(
            () => owner.Rename(TenantRoleName.Create("Superuser"), Later));

        // A state conflict, not a validation failure: nothing about the request
        // is malformed, and the same request against a custom role would succeed.
        Assert.Contains("Owner", refused.Message, StringComparison.Ordinal);
        Assert.Equal("Owner", owner.Name.Value);
        Assert.Equal(Created, owner.UpdatedAt);
    }

    [Fact]
    public void An_owners_permissions_cannot_be_replaced()
    {
        TenantRole owner = TenantRole.CreateOwner(Workspace, Created);

        Assert.Throws<DomainStateException>(
            () => owner.ReplacePermissions([PlatformPermissions.WorkspaceView], Later));

        Assert.Equal(PlatformPermissions.AllCodes, owner.EffectivePermissions);
        Assert.Equal(Created, owner.UpdatedAt);
    }

    [Fact]
    public void A_custom_role_cannot_be_created_as_a_system_role()
    {
        // There is no kind parameter, so this is a statement about the API shape
        // rather than about a check. A caller cannot ask for an Owner because
        // there is nowhere to ask.
        TenantRole custom = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Owner"),
            [],
            Created);

        // Even taking the reserved name, which nothing forbids: what makes Owner
        // Owner is its kind, and this is Custom. The database's partial unique
        // index constrains kind, not name, for the same reason.
        Assert.Equal(TenantRoleKind.Custom, custom.Kind);
        Assert.False(custom.IsSystem);
    }

    [Fact]
    public void A_custom_role_grants_exactly_what_it_was_given()
    {
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [PlatformPermissions.AiAgentsView, PlatformPermissions.AiAgentsPreview],
            Created);

        Assert.Equal(
            [PlatformPermissions.AiAgentsPreview, PlatformPermissions.AiAgentsView],
            role.EffectivePermissions);
    }

    [Fact]
    public void A_custom_role_may_grant_nothing()
    {
        // A legitimate thing to create on the way to deciding what a role should
        // do. Refusing it would only invite somebody to grant a placeholder
        // permission they then forget to remove.
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Placeholder"),
            [],
            Created);

        Assert.Empty(role.EffectivePermissions);
    }

    [Fact]
    public void Duplicate_permissions_are_deduplicated_rather_than_rejected()
    {
        // The documented contract. Asking for the same capability twice expresses
        // the same wish as asking once, and there is no reading under which it
        // means something else.
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [PlatformPermissions.RolesView, PlatformPermissions.RolesView],
            Created);

        Assert.Equal([PlatformPermissions.RolesView], role.EffectivePermissions);
        Assert.Single(role.Grants);
    }

    [Fact]
    public void An_unrecognised_permission_is_refused()
    {
        Assert.Throws<DomainValidationException>(() => TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Invented"),
            [PermissionCode.Create("integrations.manage")],
            Created));
    }

    [Fact]
    public void Replacing_permissions_swaps_the_whole_set()
    {
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [PlatformPermissions.AiAgentsView, PlatformPermissions.AiAgentsEdit],
            Created);

        role.ReplacePermissions([PlatformPermissions.RolesView], Later);

        // Replace, never merge. Merging would make removing a permission
        // impossible through this operation.
        Assert.Equal([PlatformPermissions.RolesView], role.EffectivePermissions);
        Assert.Equal(Later, role.UpdatedAt);
    }

    [Fact]
    public void A_rejected_replacement_leaves_the_previous_set_untouched()
    {
        // The whole set is validated before a single grant is removed. A loop
        // that validated and applied in one pass would empty the role and then
        // throw, which is the partial replacement this operation must never
        // produce -- and the partial state would be "grants nothing", the most
        // disruptive possible outcome.
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [PlatformPermissions.AiAgentsView],
            Created);

        Assert.Throws<DomainValidationException>(() => role.ReplacePermissions(
            [PlatformPermissions.RolesView, PermissionCode.Create("integrations.manage")],
            Later));

        Assert.Equal([PlatformPermissions.AiAgentsView], role.EffectivePermissions);
        Assert.Equal(Created, role.UpdatedAt);
    }

    [Fact]
    public void Renaming_a_custom_role_changes_the_name_and_stamps_the_clock()
    {
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [],
            Created);

        role.Rename(TenantRoleName.Create("Support supervisor"), Later);

        Assert.Equal("Support supervisor", role.Name.Value);
        Assert.Equal(Later, role.UpdatedAt);
        Assert.Equal(Created, role.CreatedAt);
    }

    [Fact]
    public void Renaming_to_the_same_name_still_moves_the_update_mark()
    {
        // The caller asked for a write and the version they hold should move.
        // Suppressing it would make a successful request look like it had not
        // happened -- and, with an ETag, would leave two clients disagreeing
        // about which version is current.
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [],
            Created);

        role.Rename(TenantRoleName.Create("Support agent"), Later);

        Assert.Equal(Later, role.UpdatedAt);
    }

    [Fact]
    public void A_role_must_belong_to_a_tenant()
    {
        Assert.Throws<DomainValidationException>(
            () => TenantRole.CreateOwner(default, Created));

        Assert.Throws<DomainValidationException>(() => TenantRole.CreateCustom(
            default,
            TenantRoleName.Create("Support agent"),
            [],
            Created));
    }

    [Fact]
    public void A_role_has_no_way_to_change_its_tenant()
    {
        // Asserted against the type rather than by attempting it: there is no
        // setter and no operation, so moving a role between workspaces -- and
        // taking its authority into a tenant that never agreed to it -- is not
        // something code can express.
        Assert.Null(typeof(TenantRole).GetProperty(nameof(TenantRole.TenantId))!.SetMethod);

        // No instance operation takes a tenant either. The static factories do,
        // because that is where a role acquires the workspace it will belong to
        // for the rest of its life; what must not exist is a way to hand an
        // existing role a different one.
        Assert.DoesNotContain(
            typeof(TenantRole).GetMethods().Where(method => !method.IsStatic),
            method => method.GetParameters().Any(
                parameter => parameter.ParameterType == typeof(TenantId)));
    }

    [Fact]
    public void New_roles_get_distinct_identities()
    {
        TenantRole first = TenantRole.CreateOwner(Workspace, Created);
        TenantRole second = TenantRole.CreateOwner(Workspace, Created);

        Assert.NotEqual(first.Id, second.Id);
        Assert.False(first.Id.IsEmpty);
    }

    [Fact]
    public void A_new_role_is_created_and_updated_at_the_same_instant()
    {
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [],
            Created);

        Assert.Equal(Created, role.CreatedAt);
        Assert.Equal(Created, role.UpdatedAt);
    }

    [Fact]
    public void Grants_name_the_role_that_carries_them()
    {
        TenantRole role = TenantRole.CreateCustom(
            Workspace,
            TenantRoleName.Create("Support agent"),
            [PlatformPermissions.AiAgentsView],
            Created);

        Assert.All(role.Grants, grant => Assert.Equal(role.Id, grant.TenantRoleId));
    }
}
