using System.Buffers;
using OmniAI.Application.Billing;

namespace OmniAI.Api.Billing;

/// <summary>
/// The route the payment provider delivers events to.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Anonymous, and therefore the route that decides who gets a plan.</strong>
/// There is no session and no user: what arrives is a POST anybody on the
/// internet can send, and the only evidence of who sent it is a signature over
/// the body. Everything below is arranged around that.
/// </para>
/// <para>
/// <strong>The signature is checked against the exact bytes, before the body is
/// read as a document.</strong> Parsing first would mean interpreting something
/// nobody has authenticated; verifying a re-serialised copy would check a
/// different document from the one acted on. The body is read once, into one
/// array, and that array is what is verified.
/// </para>
/// <para>
/// <strong>No antiforgery here, and that is correct.</strong> Antiforgery defends
/// a browser session against a cross-site request, and there is no session to
/// defend. The signature is stronger than the token a browser would have carried.
/// Every human-facing billing write is antiforgery-checked; see
/// <see cref="BillingEndpoints"/>.
/// </para>
/// <para>
/// <strong>It stores and returns.</strong> Providers redeliver anything they do
/// not get a prompt success for, so taking a workspace lock, calling back to the
/// provider and reassigning a plan all happen in a worker afterwards.
/// </para>
/// <para>
/// <strong>Nothing raw is kept and nothing is logged.</strong> The body exists
/// only long enough to be verified and reduced to a handful of identifiers.
/// </para>
/// </remarks>
internal static class BillingWebhookEndpoints
{
    /// <summary>Where the provider is pointed.</summary>
    private const string Route = "/api/billing/webhook";

    /// <summary>
    /// The largest body that will be read.
    /// </summary>
    /// <remarks>
    /// Far beyond any real event. Without a ceiling an anonymous caller could
    /// stream indefinitely into memory before the signature — computed over the
    /// whole body — could possibly be checked.
    /// </remarks>
    private const int MaxBodyBytes = 512 * 1024;

    public static IEndpointRouteBuilder MapBillingWebhookEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(Route, ReceiveAsync)
            .WithTags("Billing webhook")
            .WithName("ReceiveBillingWebhook")
            .AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// One provider event.
    /// </summary>
    /// <remarks>
    /// <strong>200 for everything genuinely signed</strong>, including a
    /// redelivery and an event type this platform does nothing with. Neither is a
    /// failure on the provider's side, and answering with an error only makes it
    /// send the same thing again for hours.
    /// </remarks>
    private static async Task<IResult> ReceiveAsync(
        HttpContext http,
        IBillingWebhookVerifier verifier,
        ReceiveBillingWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(handler);

        byte[]? rawBody = await ReadAsync(http.Request, cancellationToken).ConfigureAwait(false);

        if (rawBody is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        BillingWebhookEvent? verified = verifier.Verify(
            rawBody,
            http.Request.Headers["Stripe-Signature"].ToString());

        if (verified is null)
        {
            // Nothing has happened and nothing will: no lookup, no parse, no row,
            // no plan. 401 rather than 403 — the caller has failed to prove who
            // they are, which is different from being known and refused. Which of
            // the several possible failures it was is exactly what somebody
            // probing this route would like to be told.
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        await handler
            .HandleAsync(verifier.Provider, verified, cancellationToken)
            .ConfigureAwait(false);

        // No body, and the same answer whether the event was new or a duplicate.
        // A response describing what was stored would tell anybody who guessed a
        // signature exactly what their payload achieved.
        return Results.Ok();
    }

    /// <summary>
    /// The request body, exactly as sent.
    /// </summary>
    /// <returns>
    /// The bytes, or <see langword="null"/> when the body exceeds
    /// <see cref="MaxBodyBytes"/>.
    /// </returns>
    private static async Task<byte[]?> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        ArrayBufferWriter<byte> buffer = new();

        while (true)
        {
            Memory<byte> block = buffer.GetMemory(8192);

            int read = await request.Body.ReadAsync(block, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return buffer.WrittenSpan.ToArray();
            }

            buffer.Advance(read);

            if (buffer.WrittenCount > MaxBodyBytes)
            {
                return null;
            }
        }
    }
}
