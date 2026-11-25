using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Application.Conversations;
using OmniAI.Domain.Conversations;

namespace OmniAI.Infrastructure.Persistence.Repositories;

internal enum SimulatorSendStatus { Confirmed, Refused, Unconfirmed, Withheld }

internal static class SocialTextSender
{
    internal static async Task<SimulatorSendStatus> SendAsync(HttpMessageInvoker transport, ChannelKind channel, Guid page, string recipient,
        string text, CancellationToken token)
    {
        bool instagram = channel == ChannelKind.Instagram;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(instagram
            ? $"https://instagram.invalid/ig-account-{page:N}/messages" : $"https://messenger.invalid/{page:N}/messages"))
        {
            Content = instagram
                ? JsonContent.Create(new { recipient = new { id = recipient }, message = new { text } })
                : JsonContent.Create(new { recipient = new { id = recipient }, messaging_type = "RESPONSE", message = new { text } }),
        };
        try
        {
            using var response = await transport.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return SimulatorSendStatus.Refused;
            }

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(token).ConfigureAwait(false);
                if (body.ValueKind == JsonValueKind.Object &&
                    body.TryGetProperty("recipient_id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == recipient &&
                    body.TryGetProperty("message_id", out var mid) && mid.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(mid.GetString()))
                {
                    return SimulatorSendStatus.Confirmed;
                }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return SimulatorSendStatus.Unconfirmed; }
        catch (HttpRequestException) { return SimulatorSendStatus.Unconfirmed; }
        catch (JsonException) { return SimulatorSendStatus.Unconfirmed; }
        return SimulatorSendStatus.Unconfirmed;
    }
}

// In-process HTTP receiver. It validates the wire request and commits the customer-side receipt
// before returning (or losing) its response. No socket or provider credential is used.
internal sealed class SimulatedSocialAccount(ChannelKind channel, Guid page, string recipient, Message message, SimulatorScenario scenario,
    Func<CancellationToken, Task<int>> receive) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        bool instagram = channel == ChannelKind.Instagram;
        string host = instagram ? "instagram.invalid" : "messenger.invalid";
        string account = instagram ? $"ig-account-{page:N}" : page.ToString("N");
        if (request.RequestUri?.Host != host || request.Method != HttpMethod.Post || request.RequestUri?.AbsolutePath != $"/{account}/messages" ||
            body.GetProperty("recipient").GetProperty("id").GetString() != recipient ||
            (instagram ? body.TryGetProperty("messaging_type", out _) : body.GetProperty("messaging_type").GetString() != "RESPONSE") ||
            body.GetProperty("message").GetProperty("text").GetString() != message.Text ||
            scenario == SimulatorScenario.Refuse)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        await receive(cancellationToken).ConfigureAwait(false);
        if (scenario == SimulatorScenario.LoseConfirmation)
        {
            throw new TaskCanceledException("The simulated account accepted the reply and its confirmation timed out.");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { recipient_id = recipient, message_id = $"sim.{message.Id.Value:N}" }),
        };
    }
}
