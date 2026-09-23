using System.Text;
using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;
using OmniAI.Infrastructure.Billing;
using OmniAI.Infrastructure.Billing.Stripe;
using Stripe;

namespace OmniAI.Infrastructure.IntegrationTests.Billing;

/// <summary>
/// The Stripe adapter, with no Stripe account anywhere near it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here contacts api.stripe.com.</strong> The status mapping is a
/// pure function, and the signature check is exercised against signatures
/// generated locally with Stripe's own library — which is the point: verifying
/// with one implementation and signing with another would test this suite's idea
/// of the scheme rather than the scheme.
/// </para>
/// <para>
/// <strong>The mapping is the documented contract.</strong> Stripe's statuses and
/// this platform's are different vocabularies, and a wrong pairing here would
/// grant or revoke a plan for the wrong reason without anything else noticing.
/// </para>
/// </remarks>
public sealed class StripeStatusMappingTests
{
    [Theory]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("incomplete_expired", SubscriptionStatus.Canceled)]
    [InlineData("incomplete", SubscriptionStatus.PendingActivation)]
    [InlineData("unpaid", SubscriptionStatus.Suspended)]
    [InlineData("paused", SubscriptionStatus.Suspended)]
    public void Each_stripe_status_maps_as_documented(string raw, SubscriptionStatus expected)
    {
        Assert.Equal(expected, StripeSubscriptionStatus.Normalise(raw));
    }

    /// <remarks>
    /// A status this adapter has never heard of must not be treated as Active.
    /// Stripe may add one, and the safe reading of "I do not know what this
    /// means" is not "grant them everything".
    /// </remarks>
    [Theory]
    [InlineData("something_new")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unrecognised_status_grants_nothing(string? raw)
    {
        SubscriptionStatus mapped = StripeSubscriptionStatus.Normalise(raw);

        Assert.NotEqual(SubscriptionStatus.Active, mapped);
        Assert.NotEqual(SubscriptionStatus.Trialing, mapped);
    }
}

/// <summary>
/// Webhook verification, exercised against real signatures.
/// </summary>
public sealed class StripeWebhookVerifierTests
{
    private const string Secret = "whsec_test_verifier_local_only";

    private static readonly StripeOptions Configured = new() { WebhookSecret = Secret };

    [Fact]
    public void A_correctly_signed_body_is_read()
    {
        string body = Envelope("evt_ok", "customer.subscription.updated");

        BillingWebhookEvent? verified = new StripeWebhookVerifier(Configured)
            .Verify(Encoding.UTF8.GetBytes(body), Header(body));

        Assert.NotNull(verified);
        Assert.Equal("evt_ok", verified.EventId.Value);
        Assert.Equal("customer.subscription.updated", verified.EventType);
        Assert.Equal("sub_test_verifier", verified.SubscriptionId?.Value);
    }

    [Fact]
    public void A_body_that_changed_after_signing_is_refused()
    {
        string signed = Envelope("evt_altered", "customer.subscription.updated");
        string sent = signed.Replace("evt_altered", "evt_someone_else", StringComparison.Ordinal);

        Assert.Null(new StripeWebhookVerifier(Configured)
            .Verify(Encoding.UTF8.GetBytes(sent), Header(signed)));
    }

    [Fact]
    public void A_deployment_with_no_webhook_secret_accepts_nothing()
    {
        // Not "skip the check when unconfigured", which would make every
        // environment without a secret a way to grant plans.
        string body = Envelope("evt_unconfigured", "customer.subscription.updated");

        StripeWebhookVerifier verifier = new(new StripeOptions());

        Assert.False(verifier.CanVerify);
        Assert.Null(verifier.Verify(Encoding.UTF8.GetBytes(body), Header(body)));
    }

    [Fact]
    public void A_body_that_is_not_valid_utf8_is_refused()
    {
        // Decoded strictly rather than with replacement characters, so a body
        // that could never have been what was signed is rejected outright.
        Assert.Null(new StripeWebhookVerifier(Configured)
            .Verify([0xFF, 0xFE, 0xFD], "t=1,v1=abc"));
    }

    [Fact]
    public void A_missing_header_is_refused()
    {
        string body = Envelope("evt_no_header", "customer.subscription.updated");

        Assert.Null(new StripeWebhookVerifier(Configured)
            .Verify(Encoding.UTF8.GetBytes(body), signatureHeader: null));
    }

    /// <remarks>
    /// Nothing in this adapter reads an event's object graph beyond a handful of
    /// ids — the real state is read back from the provider afterwards — so
    /// refusing an event because an unrelated field was renamed in a newer API
    /// version would stop billing working for a change that cannot affect it.
    /// </remarks>
    [Fact]
    public void An_event_from_a_different_api_version_is_still_read()
    {
        string body = Envelope("evt_old_version", "customer.subscription.updated", "2020-08-27");

        Assert.NotNull(new StripeWebhookVerifier(Configured)
            .Verify(Encoding.UTF8.GetBytes(body), Header(body)));
    }

    private static string Header(string body) =>
        EventUtility.GenerateSignatureHeader(body, Secret);

    private static string Envelope(
        string eventId,
        string eventType,
        string apiVersion = "2026-08-26.dahlia") =>
        "{\"id\":\"" + eventId
        + "\",\"object\":\"event\",\"api_version\":\"" + apiVersion
        + "\",\"created\":1758000000,\"type\":\"" + eventType
        + "\",\"data\":{\"object\":{\"id\":\"sub_test_verifier\",\"object\":\"subscription\","
        + "\"status\":\"active\",\"customer\":\"cus_test_verifier\"}}}";
}

/// <summary>
/// The provider on a deployment with no keys.
/// </summary>
/// <remarks>
/// It exists so that a platform without a Stripe key still starts and still
/// serves conversations, agents and knowledge. Every call refuses, plainly,
/// rather than succeeding partially or fabricating a reference.
/// </remarks>
public sealed class UnconfiguredBillingProviderTests
{
    [Fact]
    public async Task Every_call_refuses()
    {
        UnconfiguredBillingProvider provider = new();

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => provider.CreateCustomerAsync("Acme", Guid.NewGuid(), "key"));

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => provider.GetSubscriptionAsync(ProviderReference.Create("sub_x")));

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => provider.GetCheckoutAsync(ProviderReference.Create("cs_x")));

        await Assert.ThrowsAsync<BillingNotConfiguredException>(
            () => provider.CreatePortalSessionAsync(
                ProviderReference.Create("cus_x"),
                "https://app.test.local/billing"));
    }
}
