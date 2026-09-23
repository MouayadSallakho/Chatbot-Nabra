using System.Text;
using OmniAI.Application.Billing;
using OmniAI.Domain.Billing;
using Stripe;

namespace OmniAI.Infrastructure.Billing.Stripe;

/// <summary>
/// Checks that a webhook really came from Stripe.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The signature is verified against the exact bytes received.</strong>
/// Stripe signs <c>&lt;timestamp&gt;.&lt;raw body&gt;</c>; any re-serialisation,
/// reformatting or whitespace change produces a different document and a failed
/// check, which is precisely the point. The bytes are decoded strictly, so a body
/// that is not valid UTF-8 is refused rather than silently mangled into something
/// that could never match.
/// </para>
/// <para>
/// <strong>The library does the cryptography.</strong> Stripe's own
/// <see cref="EventUtility"/> applies the timestamp tolerance, ignores signature
/// schemes other than <c>v1</c> and compares in constant time. Reimplementing
/// that by hand is how a timing leak or an accepted replay gets introduced.
/// </para>
/// <para>
/// <strong>An API version mismatch is not a rejection.</strong> Nothing here
/// reads the event's object graph: what is taken is a handful of ids, and the
/// actual state is read back from Stripe afterwards. Refusing an event because a
/// field elsewhere in a payload this code never touches was renamed would stop
/// billing working for a change that cannot affect it.
/// </para>
/// <para>
/// <strong>Nothing is logged and nothing raw is kept.</strong> The body exists
/// for the length of this method.
/// </para>
/// </remarks>
internal sealed class StripeWebhookVerifier(StripeOptions options) : IBillingWebhookVerifier
{
    /// <summary>Stripe's own header name. Compared case-insensitively by ASP.NET.</summary>
    public const string SignatureHeader = "Stripe-Signature";

    /// <summary>
    /// Refuses a signature whose timestamp is further away than this.
    /// </summary>
    /// <remarks>
    /// Stripe's default, and the reason a captured request cannot be replayed
    /// tomorrow. Five minutes also tolerates the clock skew a real deployment
    /// has.
    /// </remarks>
    private const long ToleranceSeconds = 300;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private readonly StripeOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    public BillingProvider Provider => BillingProvider.Stripe;

    public bool CanVerify => _options.CanReceiveWebhooks;

    public BillingWebhookEvent? Verify(byte[] rawBody, string? signatureHeader)
    {
        ArgumentNullException.ThrowIfNull(rawBody);

        // A deployment without the secret cannot tell Stripe from anybody else,
        // so it accepts nothing. Treating "no secret configured" as "skip the
        // check" would make every unconfigured environment a way to grant plans.
        if (!CanVerify || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return null;
        }

        string payload;

        try
        {
            payload = StrictUtf8.GetString(rawBody);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        Event verified;

        try
        {
            verified = EventUtility.ConstructEvent(
                payload,
                signatureHeader,
                _options.WebhookSecret,
                ToleranceSeconds,
                throwOnApiVersionMismatch: false);
        }
        catch (StripeException)
        {
            // Wrong secret, altered body, malformed header, stale timestamp. The
            // caller learns only that it failed, and so does the sender.
            return null;
        }

        return new BillingWebhookEvent(
            ProviderReference.Create(verified.Id),
            verified.Type,
            SubscriptionOf(verified),
            CustomerOf(verified),
            SessionOf(verified),
            verified.Created);
    }

    /// <summary>
    /// The subscription an event concerns, where it names one.
    /// </summary>
    /// <remarks>
    /// Only a reference is taken, never the state. A late <c>payment_failed</c>
    /// must not drag a subscription that has since recovered back into arrears,
    /// so the id is a signal to go and look rather than a fact to store.
    /// </remarks>
    private static ProviderReference? SubscriptionOf(Event verified) =>
        verified.Data?.Object switch
        {
            Subscription subscription => ProviderReference.CreateOrNull(subscription.Id),
            global::Stripe.Checkout.Session session =>
                ProviderReference.CreateOrNull(session.SubscriptionId),
            Invoice invoice => ProviderReference.CreateOrNull(SubscriptionOf(invoice)),
            _ => null,
        };

    /// <summary>
    /// An invoice's subscription, which lives on its lines.
    /// </summary>
    /// <remarks>
    /// Current API versions no longer put a subscription on the invoice itself.
    /// Reading it from where it used to be would produce a silent null on every
    /// payment event, and the arrears path would quietly never fire.
    /// </remarks>
    private static string? SubscriptionOf(Invoice invoice) =>
        invoice.Lines?.Data?
            .Select(line => line.Parent?.SubscriptionItemDetails?.Subscription)
            .FirstOrDefault(subscription => !string.IsNullOrWhiteSpace(subscription));

    private static ProviderReference? CustomerOf(Event verified) =>
        verified.Data?.Object switch
        {
            Subscription subscription => ProviderReference.CreateOrNull(subscription.CustomerId),
            global::Stripe.Checkout.Session session =>
                ProviderReference.CreateOrNull(session.CustomerId),
            Invoice invoice => ProviderReference.CreateOrNull(invoice.CustomerId),
            _ => null,
        };

    private static ProviderReference? SessionOf(Event verified) =>
        verified.Data?.Object is global::Stripe.Checkout.Session session
            ? ProviderReference.CreateOrNull(session.Id)
            : null;
}
