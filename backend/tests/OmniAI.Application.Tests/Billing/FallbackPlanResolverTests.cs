using OmniAI.Application.Billing;
using OmniAI.Application.Plans;
using OmniAI.Domain.Plans;
using OmniAI.Testing;

namespace OmniAI.Application.Tests.Billing;

/// <summary>
/// Where a workspace lands when it stops paying.
/// </summary>
/// <remarks>
/// <strong>The only rule that must never bend: it is finite.</strong> A fallback
/// that granted everything would make not paying an upgrade, and nobody would
/// notice until the provider bill arrived. That is why an unlimited plan is
/// refused here rather than accepted with a warning.
/// </remarks>
public sealed class FallbackPlanResolverTests
{
    [Fact]
    public async Task A_finite_plan_is_resolved()
    {
        Plan free = Finite("free");

        FallbackPlanResolver resolver = new(
            FakePlanCatalog.Empty().Add(free),
            new BillingOptions
            {
                FallbackPlanCode = "free",
                ReturnBaseUrl = "https://app.test.local",
            });

        Assert.Equal(free.Id, (await resolver.ResolveAsync()).Id);
    }

    /// <remarks>
    /// <c>legacy</c> and <c>development</c> are both unlimited, and both are the
    /// obvious thing to reach for. Naming either would quietly turn cancellation
    /// into an upgrade, so the resolver refuses rather than obliging.
    /// </remarks>
    [Theory]
    [InlineData("legacy")]
    [InlineData("development")]
    public async Task An_unlimited_plan_is_refused_however_it_is_named(string code)
    {
        FallbackPlanResolver resolver = new(
            FakePlanCatalog.With(code),
            new BillingOptions
            {
                FallbackPlanCode = code,
                ReturnBaseUrl = "https://app.test.local",
            });

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => resolver.ResolveAsync());
    }

    [Fact]
    public async Task A_code_naming_no_plan_is_refused()
    {
        FallbackPlanResolver resolver = new(
            FakePlanCatalog.Empty(),
            new BillingOptions
            {
                FallbackPlanCode = "nothing-like-this",
                ReturnBaseUrl = "https://app.test.local",
            });

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => resolver.ResolveAsync());
    }

    [Fact]
    public async Task A_deployment_that_configured_nothing_is_refused()
    {
        // And is a perfectly good deployment. It simply cannot sell anything, and
        // nothing about it will ever try to suspend a workspace.
        FallbackPlanResolver resolver = new(FakePlanCatalog.Empty(), new BillingOptions());

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => resolver.ResolveAsync());
    }

    [Fact]
    public void A_deployment_needs_both_a_fallback_and_a_return_address()
    {
        // Half-configured is not configured: a checkout with nowhere to return to
        // would leave the customer on the provider's page with no way back.
        Assert.False(new BillingOptions { FallbackPlanCode = "free" }.IsConfigured);

        Assert.False(
            new BillingOptions { ReturnBaseUrl = "https://app.test.local" }.IsConfigured);

        Assert.True(new BillingOptions
        {
            FallbackPlanCode = "free",
            ReturnBaseUrl = "https://app.test.local",
        }.IsConfigured);
    }

    private static Plan Finite(string code) => Plan.Create(
        PlanCode.Create(code),
        PlanDisplayName.Create("Free"),
        PlanLimit.Of(2),
        PlanLimit.Of(1),
        PlanLimit.Of(10),
        PlanLimit.Of(1),
        PlanLimit.Of(0),
        PlanLimit.Of(200),
        DateTimeOffset.UnixEpoch);
}
