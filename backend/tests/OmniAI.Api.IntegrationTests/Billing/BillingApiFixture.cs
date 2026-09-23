using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OmniAI.Api.IntegrationTests.Authorization;
using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;
using OmniAI.Domain.Plans;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;
using Stripe;

namespace OmniAI.Api.IntegrationTests.Billing;

/// <summary>
/// The real API with subscriptions configured, and no payment company involved.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What is real here.</strong> The routes, the antiforgery filter, the
/// permission checks, the Problem Details pipeline, PostgreSQL with every
/// constraint this phase added, the durable webhook inbox, the entitlement
/// assignment, and — importantly — Stripe's own signature verification, driven by
/// signatures this fixture generates locally with the same library.
/// </para>
/// <para>
/// <strong>What is substituted.</strong> One service: the outbound provider.
/// Nothing in this suite opens a socket to a payment company, no real key exists,
/// no card is entered and nothing is charged.
/// </para>
/// <para>
/// <strong>The webhook secret is a local test value.</strong> It is generated per
/// fixture instance rather than committed, for the same reason the Redis network
/// key is: a secret-shaped constant in a repository is a secret-shaped constant
/// in a repository, whatever its comment says.
/// </para>
/// </remarks>
public class BillingApiFixture : RoleApiFixture
{
    /// <summary>The plan a lapsed workspace falls back to, seeded by the migration.</summary>
    public const string FallbackPlanCode = "free";

    /// <summary>Where the provider returns the customer to. Server-owned.</summary>
    public const string ReturnBaseUrl = "https://app.test.local";

    /// <summary>A monthly offer this fixture puts on sale.</summary>
    public const string MonthlyOfferCode = "starter-monthly";

    /// <summary>An annual offer this fixture puts on sale.</summary>
    public const string AnnualOfferCode = "starter-annual";

    /// <summary>An offer that has been withdrawn.</summary>
    public const string WithdrawnOfferCode = "starter-legacy";

    private readonly string _webhookSecret = $"whsec_test_{Guid.NewGuid():N}";

    /// <summary>The provider double every test drives.</summary>
    public FakeBillingProvider Provider { get; } = new();

    /// <summary>The provider price the monthly offer is mapped to.</summary>
    public ProviderReference MonthlyPriceId { get; } = ProviderReference.Create("price_test_monthly");

    /// <summary>The provider price the annual offer is mapped to.</summary>
    public ProviderReference AnnualPriceId { get; } = ProviderReference.Create("price_test_annual");

    /// <summary>A price no offer is mapped to.</summary>
    /// <remarks>
    /// For the case that matters most: a provider billing something this platform
    /// does not sell must grant nothing, and in particular must not fall back to
    /// an unlimited plan.
    /// </remarks>
    public ProviderReference UnknownPriceId { get; } = ProviderReference.Create("price_test_unknown");

    /// <summary>The offers this fixture seeded, by code.</summary>
    public Dictionary<string, Guid> Offers { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Seeded once per fixture, however many tests ask for it.
    /// </summary>
    /// <remarks>
    /// Lazy rather than seeded during initialisation, because the offers need a
    /// migrated database and the base fixture provisions that. Once rather than
    /// per test, because the offer code is unique and a second attempt would fail
    /// on a constraint this phase deliberately added.
    /// </remarks>
    private Lazy<Task>? _offers;

    protected override IReadOnlyDictionary<string, string> HostSettings =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{BillingOptions.Section}:{nameof(BillingOptions.FallbackPlanCode)}"] =
                FallbackPlanCode,
            [$"{BillingOptions.Section}:{nameof(BillingOptions.ReturnBaseUrl)}"] = ReturnBaseUrl,
            [$"{BillingOptions.Section}:{nameof(BillingOptions.PastDueGracePeriod)}"] = "7.00:00:00",

            // A webhook secret, so the signature check is exercised for real. No
            // secret key: this deployment verifies what arrives and calls out
            // through the double.
            ["STRIPE_WEBHOOK_SECRET"] = _webhookSecret,

            // The real worker, hurried along. What it does is unchanged; it
            // simply does not make every assertion wait five seconds for the
            // next poll.
            ["Billing:Workers:IdleInterval"] = "00:00:00.200",
            ["Billing:Workers:GraceSweepInterval"] = "00:00:00.500",
        };

    /// <summary>
    /// Waits for something the billing worker will do.
    /// </summary>
    /// <remarks>
    /// Polling with a ceiling rather than a fixed sleep: the worker polls on its
    /// own schedule, so a fixed wait is either slower than it needs to be or
    /// shorter than it needs to be, and eventually both.
    /// </remarks>
    public static async Task WaitAsync(Func<Task<bool>> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(20);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("The billing worker did not reach the expected state in time.");
    }

    /// <summary>Seeds the offers and their provider price mappings.</summary>
    /// <remarks>
    /// Written directly, because there is no endpoint that creates an offer and
    /// this fixture does not invent one. A price editor exposed to make tests
    /// convenient would be a price editor shipped in the product.
    /// </remarks>
    public Task SeedOffersAsync() =>
        (_offers ??= new Lazy<Task>(SeedCoreAsync)).Value;

    private async Task SeedCoreAsync()
    {
        await using OmniAiDbContext context = CreateDbContext();

        Domain.Plans.Plan starter =
            await context.Plans.FirstAsync(plan => plan.Code == PlanCode.Create("starter"));

        BillingPlanOffer monthly = BillingPlanOffer.Create(
            starter.Id,
            OfferCode.Create(MonthlyOfferCode),
            BillingInterval.Monthly,
            Money.Of(2900, BillingCurrency.Create("USD")),
            trialDays: null,
            DateTimeOffset.UnixEpoch);

        BillingPlanOffer annual = BillingPlanOffer.Create(
            starter.Id,
            OfferCode.Create(AnnualOfferCode),
            BillingInterval.Annual,
            Money.Of(29000, BillingCurrency.Create("USD")),
            trialDays: 14,
            DateTimeOffset.UnixEpoch);

        BillingPlanOffer withdrawn = BillingPlanOffer.Create(
            starter.Id,
            OfferCode.Create(WithdrawnOfferCode),
            BillingInterval.Monthly,
            Money.Of(1900, BillingCurrency.Create("USD")),
            trialDays: null,
            DateTimeOffset.UnixEpoch);

        withdrawn.Withdraw();

        context.BillingPlanOffers.AddRange(monthly, annual, withdrawn);

        context.BillingProviderPriceMappings.AddRange(
            BillingProviderPriceMapping.Create(
                monthly.Id,
                BillingProvider.Stripe,
                MonthlyPriceId,
                DateTimeOffset.UnixEpoch),
            BillingProviderPriceMapping.Create(
                annual.Id,
                BillingProvider.Stripe,
                AnnualPriceId,
                DateTimeOffset.UnixEpoch));

        await context.SaveChangesAsync();

        Offers[MonthlyOfferCode] = monthly.Id.Value;
        Offers[AnnualOfferCode] = annual.Id.Value;
        Offers[WithdrawnOfferCode] = withdrawn.Id.Value;
    }

    /// <summary>
    /// A webhook body and the signature header that proves it.
    /// </summary>
    /// <remarks>
    /// <strong>Signed with Stripe's own library, over the exact bytes sent.</strong>
    /// That is what makes the negative cases meaningful: an altered body, a stale
    /// timestamp or a wrong secret fail because the real verifier rejects them,
    /// not because a stub was told to.
    /// </remarks>
    public HttpContent SignedWebhook(
        string eventId,
        string eventType,
        string payloadJson,
        DateTimeOffset? signedAt = null,
        string? secret = null)
    {
        string body = Envelope(eventId, eventType, payloadJson);

        StringContent content = new(body, Encoding.UTF8, "application/json");

        content.Headers.Add(
            "Stripe-Signature",
            EventUtility.GenerateSignatureHeader(
                body,
                secret ?? _webhookSecret,
                signedAt?.ToUnixTimeSeconds()));

        return content;
    }

    /// <summary>A body whose signature was computed over something else.</summary>
    /// <remarks>
    /// The one case a signature scheme exists to catch: the bytes were changed in
    /// flight. Verifying a re-serialised copy rather than the raw body is exactly
    /// how a route stops catching this, which is why the endpoint reads once into
    /// one array.
    /// </remarks>
    public HttpContent TamperedWebhook(string eventId, string eventType, string payloadJson)
    {
        string body = Envelope(eventId, eventType, payloadJson);

        StringContent content = new(
            body.Replace("\"id\":", "\"id\" :", StringComparison.Ordinal),
            Encoding.UTF8,
            "application/json");

        content.Headers.Add(
            "Stripe-Signature",
            EventUtility.GenerateSignatureHeader(body, _webhookSecret));

        return content;
    }

    /// <summary>A subscription event body naming a provider subscription.</summary>
    public static string SubscriptionPayload(ProviderReference subscriptionId, string status) =>
        "{\"id\":\"" + subscriptionId.Value
        + "\",\"object\":\"subscription\",\"status\":\"" + status + "\"}";

    /// <summary>A completed-checkout event body naming a provider session.</summary>
    public static string CheckoutPayload(ProviderReference sessionId) =>
        "{\"id\":\"" + sessionId.Value
        + "\",\"object\":\"checkout.session\",\"status\":\"complete\"}";

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        base.ConfigureTestServices(services);

        // The one substitution. Everything inbound -- signature, inbox, worker,
        // lock, entitlement assignment -- stays exactly as a deployment runs it.
        services.Replace(ServiceDescriptor.Singleton<IBillingProvider>(Provider));
    }

    /// <summary>
    /// A Stripe event envelope carrying one object.
    /// </summary>
    /// <remarks>
    /// The API version is stated but deliberately not matched to the SDK's. An
    /// event from a slightly different version must still be accepted, because
    /// nothing in this platform reads the object graph beyond a handful of ids --
    /// the real state is read back from the provider afterwards.
    /// </remarks>
    private static string Envelope(string eventId, string eventType, string payloadJson) =>
        "{\"id\":\"" + eventId
        + "\",\"object\":\"event\",\"api_version\":\"2026-08-26.dahlia\",\"created\":"
        + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        + ",\"type\":\"" + eventType
        + "\",\"data\":{\"object\":" + payloadJson + "}}";
}
