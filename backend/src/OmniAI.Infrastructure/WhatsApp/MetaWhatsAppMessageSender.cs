using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OmniAI.Application.WhatsApp;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// Sends text messages through Meta's WhatsApp Cloud API.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every Meta-specific fact in the outbound direction is in this
/// file.</strong> The path, the pinned version, the bearer header, the
/// <c>messaging_product</c> envelope, the shape of an error body. The Application
/// asks for a text to reach a recipient and is told accepted, refused or unknown.
/// </para>
/// <para>
/// <strong>Nothing is retried here.</strong> Deciding whether to send again
/// requires knowing whether the first attempt could have arrived, and only the
/// caller — holding the durable delivery row and its attempt count — can reason
/// about that. A hidden retry inside the transport is how a customer reads the
/// same reply three times.
/// </para>
/// <para>
/// <strong>Nothing here logs.</strong> Every value in scope is either a
/// credential, a phone number or a customer's message. There is no way to write a
/// useful line about this call that does not disclose one of them, so the
/// information a colleague needs travels back in the result instead.
/// </para>
/// </remarks>
internal sealed class MetaWhatsAppMessageSender(
    IHttpClientFactory clients,
    IOptions<WhatsAppTransportOptions> options)
    : IWhatsAppMessageSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _clients =
        clients ?? throw new ArgumentNullException(nameof(clients));

    private readonly WhatsAppTransportOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task<WhatsAppSendResult> SendTextAsync(
        PhoneNumberId sendFrom,
        WhatsAppCustomerId recipient,
        string text,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"{_options.GraphApiVersion}/{sendFrom.Value}/messages")
        {
            Content = JsonContent.Create(
                new SendTextRequest(
                    "whatsapp",
                    "individual",
                    recipient.Value,
                    "text",
                    new TextBody(text, PreviewUrl: false)),
                options: Json),
        };

        // The credential is set on the request and nowhere else: not on the
        // client, which is shared between workspaces, and not in any field of
        // this class.
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpClient client = _clients.CreateClient(WhatsAppTransportOptions.HttpClientName);

        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            return await InterpretAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout. The request may have been received and
            // acted on; nobody can tell from here.
            return WhatsAppSendResult.Ambiguous(
                "WhatsApp did not answer in time. Whether the message was sent is unknown.");
        }
        catch (HttpRequestException)
        {
            // A connection that failed or was dropped. Before the request was
            // written, or after it was read -- the exception does not say which.
            return WhatsAppSendResult.Ambiguous(
                "The connection to WhatsApp failed. Whether the message was sent is unknown.");
        }
    }

    /// <summary>Reads one response into one of the three outcomes.</summary>
    private static async Task<WhatsAppSendResult> InterpretAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            SendTextResponse? body = await ReadAsync<SendTextResponse>(response, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<AcceptedMessage> accepted = body?.Messages ?? [];

            string? id = accepted.Count > 0 ? accepted[0].Id : null;

            // Accepted without an id is reported as accepted with none, and the
            // caller records it as unknown: nothing could ever match a delivery
            // receipt to it, so its fate is genuinely unknowable.
            return ProviderMessageId.TryCreate(id, out ProviderMessageId providerMessageId)
                ? WhatsAppSendResult.Accepted(providerMessageId)
                : new WhatsAppSendResult(
                    WhatsAppSendKind.Accepted,
                    ProviderMessageId: null,
                    Reason: null);
        }

        // 5xx and 408 may have been raised after the message was queued, so the
        // outcome is unknown rather than failed. Every other 4xx is a decision
        // Meta made about this request, and making it again would produce the
        // same decision.
        bool ambiguous =
            (int)response.StatusCode >= 500
            || response.StatusCode == HttpStatusCode.RequestTimeout;

        string reason = await DescribeAsync(response, cancellationToken).ConfigureAwait(false);

        return ambiguous
            ? WhatsAppSendResult.Ambiguous(reason)
            : WhatsAppSendResult.Rejected(reason);
    }

    /// <summary>
    /// A sentence a colleague can act on, built from Meta's error body.
    /// </summary>
    /// <remarks>
    /// <strong>Only the fields named here are used.</strong> The raw body is never
    /// passed through: it echoes request metadata and, on some errors, the message
    /// text itself, and this string is shown in the inbox and stored in the
    /// database. The trace id is included because it is the one thing Meta's
    /// support asks for and it identifies nobody.
    /// </remarks>
    private static async Task<string> DescribeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ErrorEnvelope? envelope = await ReadAsync<ErrorEnvelope>(response, cancellationToken)
            .ConfigureAwait(false);

        MetaError? error = envelope?.Error;

        string status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);

        if (error is null)
        {
            return $"WhatsApp refused the message (HTTP {status}).";
        }

        string code = error.Code?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

        string trace = string.IsNullOrWhiteSpace(error.FbTraceId)
            ? string.Empty
            : $" Trace {error.FbTraceId}.";

        string message = string.IsNullOrWhiteSpace(error.Message)
            ? "No reason was given."
            : error.Message;

        return $"WhatsApp refused the message (HTTP {status}, code {code}). {message}{trace}";
    }

    /// <summary>
    /// Reads a body, treating anything unreadable as absent.
    /// </summary>
    /// <remarks>
    /// A body that is not the JSON expected is not worth an exception: the status
    /// code has already decided the outcome, and the body only ever refines the
    /// wording.
    /// </remarks>
    private static async Task<T?> ReadAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<T>(Json, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The Cloud API's send envelope.</summary>
    private sealed record SendTextRequest(
        [property: JsonPropertyName("messaging_product")] string MessagingProduct,
        [property: JsonPropertyName("recipient_type")] string RecipientType,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] TextBody Text);

    /// <param name="PreviewUrl">
    /// Off. Link previews make Meta fetch whatever a message links to, which
    /// turns a reply into an outbound request nobody reviewed.
    /// </param>
    private sealed record TextBody(
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("preview_url")] bool PreviewUrl);

    private sealed record SendTextResponse(
        [property: JsonPropertyName("messages")] IReadOnlyList<AcceptedMessage>? Messages);

    private sealed record AcceptedMessage([property: JsonPropertyName("id")] string? Id);

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] MetaError? Error);

    private sealed record MetaError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("code")] int? Code,
        [property: JsonPropertyName("fbtrace_id")] string? FbTraceId);
}
