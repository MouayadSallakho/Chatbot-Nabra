using OmniAI.Domain.Plans;

namespace OmniAI.Domain.Tests.Plans;

/// <summary>
/// What a plan allows, and how "unlimited" is said.
/// </summary>
/// <remarks>
/// The whole point of this type is that unlimited is an absence rather than a
/// number. Most of what follows is about keeping it that way, because every
/// sentinel bug looks exactly like arithmetic that happens to be wrong.
/// </remarks>
public sealed class PlanLimitTests
{
    [Fact]
    public void Unlimited_is_not_a_number()
    {
        PlanLimit limit = PlanLimit.Unlimited;

        Assert.True(limit.IsUnlimited);
        Assert.Null(limit.Maximum);

        // The three sentinels this design exists to avoid. If any of them ever
        // becomes the stored value, a percentage against it reads as nought and a
        // progress bar draws as empty -- and somebody eventually prints it.
        Assert.NotEqual(-1, limit.Maximum);
        Assert.NotEqual(int.MaxValue, limit.Maximum);
        Assert.NotEqual(999_999_999, limit.Maximum);
    }

    [Fact]
    public void Unlimited_always_allows_another()
    {
        Assert.True(PlanLimit.Unlimited.AllowsAnother(0));
        Assert.True(PlanLimit.Unlimited.AllowsAnother(1_000_000));
        Assert.Null(PlanLimit.Unlimited.Remaining(1_000_000));
    }

    [Fact]
    public void A_finite_limit_refuses_at_the_cap_and_not_before()
    {
        PlanLimit five = PlanLimit.Of(5);

        Assert.True(five.AllowsAnother(4));

        // At the cap, not over it. A workspace at five of five keeps its five and
        // may not have a sixth.
        Assert.False(five.AllowsAnother(5));
        Assert.False(five.AllowsAnother(6));

        Assert.Equal(1, five.Remaining(4));
        Assert.Equal(0, five.Remaining(5));

        // Never negative, even for a workspace already above a tightened cap.
        Assert.Equal(0, five.Remaining(9));
    }

    [Fact]
    public void A_limit_of_zero_is_a_real_limit_and_allows_nothing()
    {
        PlanLimit none = PlanLimit.Of(0);

        Assert.False(none.IsUnlimited);
        Assert.Equal(0, none.Maximum);
        Assert.False(none.AllowsAnother(0));
    }

    [Fact]
    public void A_negative_limit_is_refused()
    {
        // The most likely way a sentinel would get in.
        Assert.Throws<DomainValidationException>(() => PlanLimit.Of(-1));
        Assert.Throws<DomainValidationException>(() => PlanLimit.From(-1));
    }

    [Fact]
    public void Absence_reads_back_as_unlimited()
    {
        Assert.True(PlanLimit.From(null).IsUnlimited);
        Assert.Equal(7, PlanLimit.From(7).Maximum);
    }
}

/// <summary>
/// How close a workspace is to a limit, and what that is called.
/// </summary>
/// <remarks>
/// Warning and Critical are informational; only Exhausted refuses. The thresholds
/// are integer arithmetic on purpose — a floating-point percentage would put 69.9%
/// into Warning on some inputs and not others.
/// </remarks>
public sealed class EntitlementUsageTests
{
    [Theory]
    [InlineData(0, 100, 0, EntitlementState.Normal)]
    [InlineData(69, 100, 69, EntitlementState.Normal)]
    [InlineData(70, 100, 70, EntitlementState.Warning)]
    [InlineData(89, 100, 89, EntitlementState.Warning)]
    [InlineData(90, 100, 90, EntitlementState.Critical)]
    [InlineData(99, 100, 99, EntitlementState.Critical)]
    [InlineData(100, 100, 100, EntitlementState.Exhausted)]
    public void The_thresholds_are_where_they_are_documented(
        long used,
        int limit,
        int percent,
        EntitlementState expected)
    {
        EntitlementUsage usage =
            EntitlementUsage.Create(EntitlementKind.AiAgents, used, PlanLimit.Of(limit));

        Assert.Equal(percent, usage.PercentUsed);
        Assert.Equal(expected, usage.State);
    }

    [Fact]
    public void A_percentage_is_floored_rather_than_rounded()
    {
        // 2 of 3 is 66.6%, which must stay Normal. Rounding up would warn a
        // workspace that is nowhere near its limit.
        EntitlementUsage usage =
            EntitlementUsage.Create(EntitlementKind.AiAgents, 2, PlanLimit.Of(3));

        Assert.Equal(66, usage.PercentUsed);
        Assert.Equal(EntitlementState.Normal, usage.State);
    }

    [Fact]
    public void Unlimited_is_never_warned_about()
    {
        EntitlementUsage usage = EntitlementUsage.Create(
            EntitlementKind.KnowledgeDocuments,
            1_000_000,
            PlanLimit.Unlimited);

        Assert.Equal(EntitlementState.Normal, usage.State);
        Assert.False(usage.IsExhausted);

        // No percentage at all, rather than nought: nought would draw an empty
        // bar and imply a limit somewhere off to the right.
        Assert.Null(usage.PercentUsed);
        Assert.Null(usage.Remaining);
    }

    [Fact]
    public void A_limit_of_zero_is_exhausted_immediately()
    {
        EntitlementUsage usage = EntitlementUsage.Create(
            EntitlementKind.WhatsAppConnections,
            0,
            PlanLimit.Of(0));

        Assert.Equal(EntitlementState.Exhausted, usage.State);
        Assert.True(usage.IsExhausted);

        // 100 rather than a division by zero.
        Assert.Equal(100, usage.PercentUsed);
    }

    [Fact]
    public void A_workspace_above_a_tightened_limit_is_exhausted_and_not_over_a_hundred()
    {
        // Possible whenever a workspace is moved to a smaller plan. It must read
        // as full, and the percentage must not exceed 100 and break a progress bar.
        EntitlementUsage usage =
            EntitlementUsage.Create(EntitlementKind.Members, 9, PlanLimit.Of(5));

        Assert.Equal(EntitlementState.Exhausted, usage.State);
        Assert.Equal(100, usage.PercentUsed);
        Assert.Equal(0, usage.Remaining);
    }

    [Fact]
    public void Negative_usage_is_a_programming_error()
    {
        Assert.Throws<DomainValidationException>(
            () => EntitlementUsage.Create(EntitlementKind.Members, -1, PlanLimit.Of(5)));
    }
}
