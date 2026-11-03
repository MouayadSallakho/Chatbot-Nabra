using System.Security.Cryptography;
using OmniAI.Application.Authorization;
using OmniAI.Application.Invitations;
using OmniAI.Application.Invitations.ListTenantInvitations;
using OmniAI.Application.Tests.Authorization;
using OmniAI.Application.Tests.Security;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Team;

/// <summary>
/// Phase 37.5b, row 24: the invitation list's status filter, and the end of its N+1.
/// </summary>
public sealed class InvitationListTests
{
    private static readonly Guid Workspace = Guid.CreateVersion7();
    private static readonly TenantId Tenant = TenantId.From(Workspace);
    private static readonly UserId Owner = UserId.New();
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Pending", 2)]
    [InlineData("pending", 2)]
    [InlineData("Expired", 1)]
    [InlineData("Revoked", 1)]
    [InlineData("Accepted", 0)]
    [InlineData(null, 4)]
    [InlineData("", 4)]
    public async Task The_status_filter_narrows_the_page_and_its_total(string? status, int expected)
    {
        World world = new();

        TenantInvitationPage? page = await world.Handler()
            .HandleAsync(new ListTenantInvitationsQuery(Workspace, Status: status));

        Assert.NotNull(page);
        Assert.Equal(expected, page.Total);
        Assert.Equal(expected, page.Items.Count);
    }

    [Fact]
    public async Task A_pending_invitation_past_its_expiry_is_listed_as_expired()
    {
        World world = new();

        TenantInvitationPage? expired = await world.Handler()
            .HandleAsync(new ListTenantInvitationsQuery(Workspace, Status: "Expired"));

        TenantInvitationView stale = Assert.Single(expired!.Items);
        Assert.Equal(TenantInvitationStatus.Expired, stale.Status);
        Assert.Equal(world.Stale.Id.Value, stale.Id);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("Pending,Accepted")]
    [InlineData("Pend ing")]
    public async Task An_unknown_status_is_refused_before_anything_is_read(string status)
    {
        // Refused for an outsider too: the check comes before membership, so the
        // answer says nothing about the workspace.
        World world = new();

        await Assert.ThrowsAsync<InvitationStatusFilterInvalidException>(() => world
            .Handler(TestAuthority.Outsider(Owner))
            .HandleAsync(new ListTenantInvitationsQuery(Workspace, Status: status)));

        Assert.Equal(0, world.Roles.NameReads);
        Assert.Equal(0, world.Accounts.BatchReads);
    }

    [Fact]
    public async Task An_outsider_with_a_valid_status_is_answered_as_a_missing_workspace()
    {
        World world = new();

        Assert.Null(await world
            .Handler(TestAuthority.Outsider(Owner))
            .HandleAsync(new ListTenantInvitationsQuery(Workspace, Status: "Pending")));
    }

    [Fact]
    public async Task A_page_reads_role_names_and_inviters_once_whatever_its_length()
    {
        World world = new();

        for (int extra = 0; extra < 9; extra++)
        {
            world.Seed(Now.AddMinutes(-extra), Now.AddDays(5));
        }

        TenantInvitationPage? page = await world.Handler()
            .HandleAsync(new ListTenantInvitationsQuery(Workspace, PageSize: 100));

        Assert.Equal(13, page!.Items.Count);
        Assert.Equal(1, world.Roles.NameReads);
        Assert.Equal(1, world.Accounts.BatchReads);
        Assert.All(page.Items, item =>
        {
            Assert.Equal(["Support"], item.Roles);
            Assert.Equal("owner@example.test", item.InvitedByEmail);
        });
    }

    [Fact]
    public async Task A_role_that_has_gone_is_left_out_and_the_names_are_sorted()
    {
        World world = new();
        TenantRole agents = world.Roles.AddRole(TenantRole.CreateCustom(
            Tenant,
            TenantRoleName.Create("agents"),
            [PlatformPermissions.AiAgentsView],
            Now));
        TenantRole gone = world.Roles.AddRole(TenantRole.CreateCustom(
            Tenant,
            TenantRoleName.Create("Gone"),
            [PlatformPermissions.AiAgentsView],
            Now));
        TenantInvitation invitation = world.Seed(Now.AddMinutes(1), Now.AddDays(5), world.Support.Id, gone.Id, agents.Id);
        world.Roles.Forget(gone);

        TenantInvitationPage? page = await world.Handler().HandleAsync(new ListTenantInvitationsQuery(Workspace));

        Assert.Equal(["agents", "Support"], Assert.Single(page!.Items, item => item.Id == invitation.Id.Value).Roles);
    }

    /// <summary>
    /// A workspace with two pending invitations, one stored-pending past its expiry,
    /// and one revoked.
    /// </summary>
    private sealed class World
    {
        public World()
        {
            Support = Roles.AddRole(TenantRole.CreateCustom(
                Tenant,
                TenantRoleName.Create("Support"),
                [PlatformPermissions.AiAgentsView],
                Now));

            Accounts.With(Owner, "owner@example.test");

            Seed(Now.AddHours(-1), Now.AddDays(6));
            Seed(Now.AddHours(-2), Now.AddDays(6));
            Stale = Seed(Now.AddDays(-9), Now.AddDays(-2));

            TenantInvitation revoked = Seed(Now.AddHours(-3), Now.AddDays(6));
            revoked.Revoke(Now.AddHours(-1));
        }

        public RecordingRoleRepository Roles { get; } = new();

        public InMemoryAccountDirectory Accounts { get; } = new();

        public InMemoryInvitationRepository Invitations { get; } = new();

        public TenantRole Support { get; }

        public TenantInvitation Stale { get; }

        public TenantInvitation Seed(DateTimeOffset createdAt, DateTimeOffset expiresAt, params TenantRoleId[] roles) =>
            Invitations.Seed(TenantInvitation.Issue(
                Tenant,
                EmailAddress.Create($"invitee-{Guid.NewGuid():N}@example.test"),
                Owner,
                InvitationTokenHash.From(RandomNumberGenerator.GetBytes(InvitationTokenHash.Length)),
                roles.Length == 0 ? [Support.Id] : roles,
                createdAt,
                expiresAt));

        public ListTenantInvitationsHandler Handler(TenantAuthorizer? authorizer = null) =>
            new(
                Invitations,
                Roles,
                Accounts,
                authorizer ?? TestAuthority.Owner(Owner, Workspace),
                new FixedTimeProvider(Now));
    }
}
