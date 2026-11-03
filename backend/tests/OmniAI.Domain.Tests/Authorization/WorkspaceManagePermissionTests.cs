using OmniAI.Domain.Authorization;
using OmniAI.Domain.Security;
using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Tests.Authorization;

/// <summary>Phase 37.5a, row 20a: the <c>workspace.manage</c> permission and its audit kind.</summary>
public sealed class WorkspaceManagePermissionTests
{
    [Fact]
    public void The_catalogue_offers_workspace_manage_in_the_workspace_group()
    {
        PermissionDefinition? definition = PlatformPermissions.Find(PlatformPermissions.WorkspaceManage);

        Assert.NotNull(definition);
        Assert.Equal("workspace.manage", definition.Code.Value);
        Assert.Equal(PlatformPermissions.WorkspaceGroup, definition.Group);
    }

    [Fact]
    public void The_owner_holds_it_with_no_data_step()
    {
        TenantRole owner = TenantRole.CreateOwner(TenantId.New(), DateTimeOffset.UnixEpoch);

        Assert.Contains(PlatformPermissions.WorkspaceManage, owner.EffectivePermissions);
    }

    [Fact]
    public void It_implies_nothing_and_nothing_implies_it()
    {
        TenantRole viewer = TenantRole.CreateCustom(
            TenantId.New(),
            TenantRoleName.Create("Viewer"),
            [PlatformPermissions.WorkspaceView],
            DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain(PlatformPermissions.WorkspaceManage, viewer.EffectivePermissions);
    }

    [Fact]
    public void A_rename_is_a_known_security_event_kind()
    {
        Assert.True(SecurityEventKinds.IsKnown("workspace.renamed"));

        SecurityEvent recorded = SecurityEvent.Record(
            SecurityEventKinds.WorkspaceRenamed,
            DateTimeOffset.UnixEpoch,
            tenantId: TenantId.New());

        Assert.Equal("workspace.renamed", recorded.Kind);
    }

    [Fact]
    public void A_refused_workspace_name_carries_its_code()
    {
        DomainValidationException refused = Assert.Throws<DomainValidationException>(() => TenantName.Create(" "));

        Assert.Equal("workspace.name.invalid", refused.Code);
    }
}
