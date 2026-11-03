using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Authorization;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Identity;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Phase 37.5b: the reads the redesigned administration screens asked for. How many
/// members hold a role (row 23), the invitation status filter and its N+1 fix
/// (row 24), each scoped by its workspace in the query and free of per-row reads.
/// </summary>
public sealed class AdministrationReadModelTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    // ---- roles (row 23) ----------------------------------------------------

    [Fact]
    public async Task The_role_list_counts_each_role_s_members_in_one_statement()
    {
        NewWorkspace workspace = await ProvisionAsync();
        TenantRole support = await StoreRoleAsync(workspace, "Support");
        TenantRole empty = await StoreRoleAsync(workspace, "Nobody yet");

        await AddMemberAsync(workspace.Tenant.Id, support);
        await AddMemberAsync(workspace.Tenant.Id, support);

        RecordedSql recorded = new();
        await using OmniAiDbContext context = database.CreateContext(recorded);

        IReadOnlyList<TenantRoleSummary> roles = await new TenantRoleRepository(context).ListAsync(workspace.Tenant.Id);

        Assert.Single(recorded.Statements);
        Assert.Equal(1, Assert.Single(roles, role => role.Kind == TenantRoleKind.Owner).MemberCount);
        Assert.Equal(2, Assert.Single(roles, role => role.Id == support.Id.Value).MemberCount);
        Assert.Equal(0, Assert.Single(roles, role => role.Id == empty.Id.Value).MemberCount);
    }

    [Fact]
    public async Task A_role_s_member_count_is_read_only_in_its_own_workspace()
    {
        NewWorkspace workspace = await ProvisionAsync();
        NewWorkspace other = await ProvisionAsync();
        TenantRole support = await StoreRoleAsync(workspace, "Support");
        await AddMemberAsync(workspace.Tenant.Id, support);

        await using OmniAiDbContext context = database.CreateContext();
        TenantRoleRepository repository = new(context);

        Assert.Equal(1, await repository.CountMembersAsync(workspace.Tenant.Id, support.Id));
        Assert.Equal(0, await repository.CountMembersAsync(other.Tenant.Id, support.Id));
    }

    [Fact]
    public async Task Role_names_are_read_in_one_statement_and_only_from_the_workspace()
    {
        NewWorkspace workspace = await ProvisionAsync();
        NewWorkspace other = await ProvisionAsync();
        TenantRole support = await StoreRoleAsync(workspace, "Support");
        TenantRole elsewhere = await StoreRoleAsync(other, "Elsewhere");

        RecordedSql recorded = new();
        await using OmniAiDbContext context = database.CreateContext(recorded);

        IReadOnlyDictionary<TenantRoleId, string> names = await new TenantRoleRepository(context)
            .FindNamesAsync(workspace.Tenant.Id, [support.Id, elsewhere.Id, TenantRoleId.New(), support.Id]);

        Assert.Single(recorded.Statements);
        Assert.Equal("Support", Assert.Single(names).Value);
        Assert.False(names.ContainsKey(elsewhere.Id));
    }

    // ---- invitations (row 24) ----------------------------------------------

    [Theory]
    [InlineData(null, 5)]
    [InlineData(TenantInvitationStatus.Pending, 1)]
    [InlineData(TenantInvitationStatus.Expired, 2)]
    [InlineData(TenantInvitationStatus.Accepted, 1)]
    [InlineData(TenantInvitationStatus.Revoked, 1)]
    public async Task The_status_filter_follows_the_status_the_list_shows(TenantInvitationStatus? status, int expected)
    {
        (TenantId workspace, _) = await InvitationsAsync();

        await using OmniAiDbContext context = database.CreateContext();
        TenantInvitationRepository repository = new(context);

        IReadOnlyList<TenantInvitation> page = await repository.ListAsync(workspace, status, Now, 0, 20);

        Assert.Equal(expected, page.Count);
        Assert.Equal(expected, await repository.CountAsync(workspace, status, Now));

        if (status is not null)
        {
            Assert.All(page, invitation =>
                Assert.Equal(status, invitation.HasExpiredAt(Now) ? TenantInvitationStatus.Expired : invitation.Status));
        }
    }

    [Fact]
    public async Task A_pending_invitation_past_its_expiry_is_expired_and_not_pending()
    {
        (TenantId workspace, TenantInvitation stale) = await InvitationsAsync();

        await using OmniAiDbContext context = database.CreateContext();
        TenantInvitationRepository repository = new(context);

        Assert.Equal(TenantInvitationStatus.Pending, stale.Status);
        Assert.Contains(await repository.ListAsync(workspace, TenantInvitationStatus.Expired, Now, 0, 20), invitation => invitation.Id == stale.Id);
        Assert.DoesNotContain(await repository.ListAsync(workspace, TenantInvitationStatus.Pending, Now, 0, 20), invitation => invitation.Id == stale.Id);
    }

    [Fact]
    public async Task The_status_filter_never_reaches_another_workspace()
    {
        await InvitationsAsync();
        NewWorkspace empty = await ProvisionAsync();

        await using OmniAiDbContext context = database.CreateContext();
        TenantInvitationRepository repository = new(context);

        foreach (TenantInvitationStatus? status in (TenantInvitationStatus?[])[null, .. Enum.GetValues<TenantInvitationStatus>()])
        {
            Assert.Empty(await repository.ListAsync(empty.Tenant.Id, status, Now, 0, 20));
            Assert.Equal(0, await repository.CountAsync(empty.Tenant.Id, status, Now));
        }
    }

    [Fact]
    public async Task Inviter_addresses_are_read_in_one_statement()
    {
        UserId first = await TestIdentity.SeedUserAsync(database);
        UserId second = await TestIdentity.SeedUserAsync(database);

        RecordedSql recorded = new();
        await using OmniAiDbContext context = database.CreateContext(recorded);

        IReadOnlyDictionary<UserId, EmailAddress> addresses = await new IdentityAccountDirectory(context)
            .FindEmailsAsync([first, second, UserId.New(), first]);

        Assert.Single(recorded.Statements);
        Assert.Equal(2, addresses.Count);
        Assert.Equal($"user-{first.Value:N}@example.test", addresses[first].Value);
    }

    [Fact]
    public async Task An_account_without_a_normalised_address_has_no_usable_address_in_a_batch_either()
    {
        UserId seeded = await TestIdentity.SeedUserAsync(database);

        await using (OmniAiDbContext clearing = database.CreateContext())
        {
            await clearing.Set<OmniAiUser>()
                .Where(user => user.Id == seeded.Value)
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.NormalizedEmail, (string?)null));
        }

        await using OmniAiDbContext context = database.CreateContext();
        IdentityAccountDirectory directory = new(context);

        Assert.Null(await directory.FindEmailAsync(seeded));
        Assert.Empty(await directory.FindEmailsAsync([seeded]));
    }

    // ---- arranging ---------------------------------------------------------

    /// <summary>
    /// One invitation in each state, plus a stored-pending one past its expiry.
    /// </summary>
    private async Task<(TenantId Workspace, TenantInvitation StalePending)> InvitationsAsync()
    {
        NewWorkspace workspace = await ProvisionAsync();
        TenantId tenantId = workspace.Tenant.Id;
        UserId inviter = workspace.OwnerAssignment.UserId;
        TenantRoleId[] roles = [workspace.OwnerRole.Id];

        TenantInvitation pending = Issue(tenantId, inviter, roles, Now.AddHours(-1), Now.AddDays(6));
        TenantInvitation stale = Issue(tenantId, inviter, roles, Now.AddDays(-9), Now.AddDays(-2));
        TenantInvitation expired = Issue(tenantId, inviter, roles, Now.AddDays(-9), Now.AddDays(-2));
        expired.Expire(Now);

        TenantInvitation accepted = Issue(tenantId, inviter, roles, Now.AddHours(-2), Now.AddDays(6));
        accepted.Accept(await TestIdentity.SeedUserAsync(database), Now.AddHours(-1));

        TenantInvitation revoked = Issue(tenantId, inviter, roles, Now.AddHours(-3), Now.AddDays(6));
        revoked.Revoke(Now.AddHours(-1));

        foreach (TenantInvitation invitation in (TenantInvitation[])[pending, stale, expired, accepted, revoked])
        {
            await using OmniAiDbContext context = database.CreateContext();
            await new TenantInvitationRepository(context).AddAsync(invitation, Now, null);
        }

        return (tenantId, stale);
    }

    private static TenantInvitation Issue(
        TenantId tenantId,
        UserId inviter,
        TenantRoleId[] roles,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) =>
        TenantInvitation.Issue(
            tenantId,
            EmailAddress.Create($"invitee-{Guid.NewGuid():N}@example.test"),
            inviter,
            InvitationTokenHash.From(RandomNumberGenerator.GetBytes(InvitationTokenHash.Length)),
            roles,
            createdAt,
            expiresAt);

    private async Task<NewWorkspace> ProvisionAsync()
    {
        NewWorkspace workspace = NewWorkspace.Create(
            TenantName.Create("Acme Corporation"),
            TenantSlug.Create($"t{Guid.NewGuid():N}"[..20]),
            await TestIdentity.SeedUserAsync(database),
            SeededPlans.Legacy,
            Now);

        await using OmniAiDbContext context = database.CreateContext();
        await new TenantRepository(context).AddAsync(workspace);

        return workspace;
    }

    private async Task<TenantRole> StoreRoleAsync(NewWorkspace workspace, string name)
    {
        TenantRole role = TenantRole.CreateCustom(
            workspace.Tenant.Id,
            TenantRoleName.Create(name),
            [PlatformPermissions.AiAgentsView],
            Now);

        await using OmniAiDbContext context = database.CreateContext();
        await new TenantRoleRepository(context).AddAsync(role);

        return role;
    }

    /// <summary>A new member of the workspace holding one role.</summary>
    private async Task AddMemberAsync(TenantId tenantId, TenantRole role)
    {
        UserId member = await TestIdentity.SeedUserAsync(database);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            context.TenantMemberships.Add(TenantMembership.Create(member, tenantId, Now));
            await context.SaveChangesAsync();
        }

        await using OmniAiDbContext assigning = database.CreateContext();
        assigning.TenantRoleAssignments.Add(TenantRoleAssignment.ForRole(member, role, Now));
        await assigning.SaveChangesAsync();
    }
}
