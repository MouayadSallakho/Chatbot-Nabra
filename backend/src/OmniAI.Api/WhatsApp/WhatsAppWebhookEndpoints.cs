using System.Buffers;
using Microsoft.AspNetCore.Mvc;
using OmniAI.Application.WhatsApp;

namespace OmniAI.Api.WhatsApp;

/// <summary>
/// The route Meta delivers WhatsApp events to.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Anonymous, and therefore the most exposed route on this
/// platform.</strong> There is no session, no cookie and no user: what arrives is
/// a POST anybody on the internet can send, and the only evidence of who sent it
/// is an HMAC over the body. Everything below is arranged around that one fact.
/// </para>
/// <para>
/// <strong>The signature is checked before the body is parsed.</strong> Not
/// before it is acted on — before it is <em>read</em>. Parsing first would mean
/// interpreting a document nobody has authenticated, and re-serialising a parsed
/// payload to verify it would check a different document from the one that gets
/// acted on.
/// </para>
/// <para>
/// <strong>It answers immediately.</strong> Meta redelivers anything it does not
/// get a prompt success for, so this verifies, writes durable rows and returns.
/// Retrieval, generation and the outbound call happen in workers.
/// </para>
/// <para>
/// <strong>Nothing here is antiforgery-checked, and that is correct.</strong>
/// Antiforgery defends a browser session against a cross-site request; there is
/// no session here to defend. The signature is the whole of the authentication,
/// and it is stronger than a token a browser would have carried.
/// </para>
/// </remarks>
internal static class WhatsAppWebhookEndpoints
{
    /// <summary>Where Meta is pointed.</summary>
    private const string RoutePrefix = "/api/whatsapp/webhook";

    /// <summary>
    /// The largest body that will be read.
    /// </summary>
    /// <remarks>
    /// A megabyte is far beyond any batch of text messages. Without a ceiling,
    /// an anonymous caller could stream indefinitely into memory before the
    /// signature — computed over the whole body — could possibly be checked.
    /// </remarks>
    private const int MaxBodyBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapWhatsAppWebhookEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder webhook = endpoints
            .MapGroup(RoutePrefix)
            .WithTags("WhatsApp webhook")
            .AllowAnonymous();

        webhook.MapGet("/", VerifyAsync)
            .WithName("VerifyWhatsAppWebhook");

        webhook.MapPost("/", ReceiveAsync)
            .WithName("ReceiveWhatsAppWebhook");

        return endpoints;
    }

    /// <summary>
    /// Meta's subscription challenge.
    /// </summary>
    /// <remarks>
    /// Answered with the challenge as <c>text/plain</c> and nothing else. Meta
    /// compares the body byte for byte, so a JSON wrapper or a trailing newline
    /// fails the subscription with no useful diagnostic anywhere.
    /// </remarks>
    private static Task<IResult> VerifyAsync(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IWhatsAppWebhookAuthenticator authenticator)
    {
        ArgumentNullException.ThrowIfNull(authenticator);

        // All three are required. A request missing any of them is not a
        // subscription attempt, and answering it as one would echo arbitrary
        // text to an anonymous caller.
        bool valid =
            string.Equals(mode, "subscribe", StringComparison.Ordinal)
            && !string.IsNullOrEmpty(challenge)
            && authenticator.IsVerifyTokenValid(verifyToken);

        // 403 for every failure, with no body. Which of the three was wrong is
        // exactly what somebody probing this route would like to be told.
        return Task.FromResult(valid
            ? Results.Text(challenge!, "text/plain")
            : Results.StatusCode(StatusCodes.Status403Forbidden));
    }

    /// <summary>
    /// One batch of WhatsApp events.
    /// </summary>
    /// <remarks>
    /// <strong>200 for everything that is genuinely from Meta</strong>, including
    /// payloads this platform does nothing with: an unknown number, a disabled
    /// connection, a message type this phase does not accept, a shape from a newer
    /// API version. None of those is an error on Meta's side, and answering with a
    /// failure only makes it send the same thing again for hours.
    /// </remarks>
    private static async Task<IResult> ReceiveAsync(
        HttpContext http,
        IWhatsAppWebhookAuthenticator authenticator,
        IWhatsAppWebhookParser parser,
        ReceiveWhatsAppWebhookHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(handler);

        byte[]? rawBody = await ReadAsync(http.Request, cancellationToken).ConfigureAwait(false);

        if (rawBody is null)
        {
            // Larger than the ceiling. Refused without reading the rest.
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string? signature = http.Request.Headers["X-Hub-Signature-256"].ToString();

        if (!authenticator.IsSignatureValid(rawBody, signature))
        {
            // <strong>Nothing has happened yet and nothing will.</strong> No
            // lookup, no parse, no row, no AI, no send. A 401 rather than a 403:
            // the caller has failed to prove who they are, which is a different
            // thing from being known and refused.
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        // Only now is the body a document worth reading.
        WhatsAppWebhook webhook = parser.Parse(rawBody);

        await handler.HandleAsync(webhook, cancellationToken).ConfigureAwait(false);

        // No body. Meta reads the status code and nothing else, and a response
        // describing what was stored would tell an attacker who guessed a
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
    /// <remarks>
    /// <strong>Read once, into one array, and both the signature check and the
    /// parse use that array.</strong> Reading the body twice — or verifying a
    /// re-serialised copy — would mean signing one document and acting on
    /// another, which is the classic way a signed-webhook route is defeated.
    /// </remarks>
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
