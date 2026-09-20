using OmniAI.Domain;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Tenants;

/// <summary>
/// What a usable workspace consists of.
/// </summary>
/// <remarks>
/// Every test here is about the same property: the four rows cannot disagree,
/// because they are derived rather than supplied. That is what replaced the
/// run-time consistency check the repository used to perform.
/// </remarks>
public sealed class NewWorkspaceTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 6, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_workspace_carries_a_tenant_a_membership_an_owner_role_and_an_assignment()
    {
        // A workspace missing any one of these is broken in a way no endpoint can
        // repair: no membership means the creator cannot reach it; no Owner role
        // means there is no full authority in it and no way to mint one; no
        // assignment means the creator may do nothing in a workspace where nobody
        // can grant them anything.
        NewWorkspace workspace = Provision(UserId.New());

        Assert.NotNull(workspace.Tenant);
        Assert.NotNull(workspace.Membership);
        Assert.NotNull(workspace.OwnerRole);
        Assert.NotNull(workspace.OwnerAssignment);
    }

    [Fact]
    public void All_four_rows_name_the_same_tenant()
    {
        NewWorkspace workspace = Provision(UserId.New());

        Assert.Equal(workspace.Tenant.Id, workspace.Membership.TenantId);
        Assert.Equal(workspace.Tenant.Id, workspace.OwnerRole.TenantId);
        Assert.Equal(workspace.Tenant.Id, workspace.OwnerAssignment.TenantId);
    }

    [Fact]
    public void The_membership_and_the_assignment_name_the_same_person()
    {
        UserId creator = UserId.New();

        NewWorkspace workspace = Provision(creator);

        Assert.Equal(creator, workspace.Membership.UserId);
        Assert.Equal(creator, workspace.OwnerAssignment.UserId);
    }

    [Fact]
    public void The_assignment_names_the_owner_role_that_was_created()
    {
        NewWorkspace workspace = Provision(UserId.New());

        Assert.Equal(workspace.OwnerRole.Id, workspace.OwnerAssignment.TenantRoleId);
    }

    [Fact]
    public void The_role_is_the_protected_owner_and_grants_everything()
    {
        // The creator becomes an Owner rather than a member with a chosen set of
        // permissions, and that is not a default that could reasonably have been
        // something else: somebody has to be able to administer a new workspace,
        // and they are the only person in it.
        NewWorkspace workspace = Provision(UserId.New());

        Assert.True(workspace.OwnerRole.IsSystem);
        Assert.Equal(TenantRoleKind.Owner, workspace.OwnerRole.Kind);
        Assert.Equal(PlatformPermissions.AllCodes, workspace.OwnerRole.EffectivePermissions);
    }

    [Fact]
    public void One_instant_is_stamped_on_everything_that_carries_one()
    {
        // Three readings of a clock taken microseconds apart would put three
        // different timestamps on what is a single act.
        NewWorkspace workspace = Provision(UserId.New());

        Assert.Equal(Created, workspace.Membership.CreatedAt);
        Assert.Equal(Created, workspace.OwnerRole.CreatedAt);
        Assert.Equal(Created, workspace.OwnerRole.UpdatedAt);
        Assert.Equal(Created, workspace.OwnerAssignment.CreatedAt);
    }

    [Fact]
    public void The_tenant_starts_active_with_the_name_and_slug_it_was_given()
    {
        NewWorkspace workspace = NewWorkspace.Create(TenantName.Create("Acme Corporation"), TenantSlug.Create("acme"), UserId.New(), PlanId.New(), Created);

        Assert.Equal("Acme Corporation", workspace.Tenant.Name.Value);
        Assert.Equal("acme", workspace.Tenant.Slug.Value);
        Assert.Equal(TenantStatus.Active, workspace.Tenant.Status);
    }

    [Fact]
    public void Two_workspaces_get_distinct_identities_throughout()
    {
        NewWorkspace first = Provision(UserId.New());
        NewWorkspace second = Provision(UserId.New());

        Assert.NotEqual(first.Tenant.Id, second.Tenant.Id);
        Assert.NotEqual(first.OwnerRole.Id, second.OwnerRole.Id);
    }

    [Fact]
    public void A_creator_without_an_identity_is_rejected()
    {
        Assert.Throws<DomainValidationException>(() => NewWorkspace.Create(TenantName.Create("Acme Corporation"), TenantSlug.Create("acme"), default, PlanId.New(), Created));
    }

    [Fact]
    public void A_missing_name_or_slug_is_a_programming_error()
    {
        Assert.Throws<ArgumentNullException>(() => NewWorkspace.Create(null!, TenantSlug.Create("acme"), UserId.New(), PlanId.New(), Created));

        Assert.Throws<ArgumentNullException>(() => NewWorkspace.Create(TenantName.Create("Acme Corporation"), null!, UserId.New(), PlanId.New(), Created));
    }

    private static NewWorkspace Provision(UserId creator) => NewWorkspace.Create(TenantName.Create("Acme Corporation"), TenantSlug.Create("acme"), creator, PlanId.New(), Created);
}
