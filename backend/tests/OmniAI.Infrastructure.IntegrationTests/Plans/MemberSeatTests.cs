using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Plans;

/// <summary>
/// Seats, and who gets the last one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A pending invitation is not a seat.</strong> Counting one would let an
/// administrator lock their own team out by inviting people who never arrive, and
/// revoking would be the only way back. The seat is taken when somebody actually
/// joins.
/// </para>
/// <para>
/// <strong>Which means several invitations can outlive one free seat.</strong>
/// That is the interesting case, and the one this suite is mostly about: they may
/// all exist, and exactly one acceptance may succeed.
/// </para>
/// </remarks>
public sealed class MemberSeatTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    [Fact]
    public async Task Members_are_counted_from_the_memberships_that_exist()
    {
        PlanWorld world = await New(members: 5);

        await world.AddMembersAsync(3);

        WorkspaceEntitlements entitlements = await Describe(world);

        Assert.Equal(3, entitlements.Find(EntitlementKind.Members)!.Used);
        Assert.Equal(2, entitlements.Find(EntitlementKind.Members)!.Remaining);
    }

    [Fact]
    public async Task A_workspace_at_its_seat_limit_admits_nobody_else()
    {
        PlanWorld world = await New(members: 2);

        await world.AddMembersAsync(2);

        await Assert.ThrowsAsync<EntitlementExceededException>(() => Join(world));

        Assert.Equal(2, await world.CountMembersAsync());
    }

    /// <remarks>
    /// <strong>The case the spec calls out.</strong> One seat, two people holding
    /// usable invitations, both accepting at the same moment. One gets in; the
    /// other is refused and no membership is created for them.
    /// </remarks>
    [Fact]
    public async Task Two_acceptances_racing_for_the_last_seat_admit_exactly_one()
    {
        PlanWorld world = await New(members: 3);

        await world.AddMembersAsync(2);

        Exception?[] outcomes = await Outcomes(Join(world), Join(world));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is EntitlementExceededException);

        Assert.Equal(3, await world.CountMembersAsync());
    }

    [Fact]
    public async Task Five_acceptances_racing_for_one_seat_admit_exactly_one()
    {
        PlanWorld world = await New(members: 1);

        Exception?[] outcomes = await Outcomes(
            [.. Enumerable.Range(0, 5).Select(_ => Join(world))]);

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Equal(4, outcomes.Count(outcome => outcome is EntitlementExceededException));

        Assert.Equal(1, await world.CountMembersAsync());
    }

    [Fact]
    public async Task An_unlimited_plan_admits_everybody()
    {
        PlanWorld world = await New();

        Exception?[] outcomes = await Outcomes(
            [.. Enumerable.Range(0, 4).Select(_ => Join(world))]);

        Assert.All(outcomes, Assert.Null);
        Assert.Equal(4, await world.CountMembersAsync());
    }

    private async Task<PlanWorld> New(int? members = null) =>
        await new PlanWorld(database).StartAsync(members: members);

    private static async Task<WorkspaceEntitlements> Describe(PlanWorld world)
    {
        (IWorkspaceEntitlementService service, OmniAiDbContext context) = world.Service();

        await using (context)
        {
            return await service.DescribeAsync(world.Tenant.Id);
        }
    }

    /// <summary>Takes a seat and writes the membership that uses it.</summary>
    /// <remarks>
    /// The account is created first, outside the reservation, exactly as it is in
    /// production: somebody has an account before they accept an invitation.
    /// </remarks>
    private static async Task<int> Join(PlanWorld world)
    {
        UserId userId = await world.NewAccountAsync();

        (IWorkspaceEntitlementService service, OmniAiDbContext context) = world.Service();

        await using (context)
        {
            return await service.ReserveAsync(
                world.Tenant.Id,
                EntitlementKind.Members,
                async token =>
                {
                    context.TenantMemberships.Add(
                        TenantMembership.Create(userId, world.Tenant.Id, DateTimeOffset.UtcNow));

                    return await context.SaveChangesAsync(token);
                });
        }
    }

    private static async Task<Exception?[]> Outcomes(params Task<int>[] attempts)
    {
        Exception?[] outcomes = new Exception?[attempts.Length];

        for (int index = 0; index < attempts.Length; index++)
        {
            try
            {
                await attempts[index];
            }
            catch (Exception failure)
            {
                outcomes[index] = failure;
            }
        }

        return outcomes;
    }
}
