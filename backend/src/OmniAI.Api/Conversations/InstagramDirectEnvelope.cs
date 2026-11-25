using System.Text.Json.Serialization;
using OmniAI.Application.Conversations;
using OmniAI.Domain;

namespace OmniAI.Api.Conversations;

// Authenticated local fixtures for a simulated Instagram professional account.
internal sealed record InstagramDirectEnvelope(
    [property: JsonPropertyName("object")] string? ObjectType, InstagramAccountEntry?[]? Entry)
{
    public IReadOnlyList<SimulatorInboundText> ReadTexts(Guid agent)
    {
        string account = $"ig-account-{agent:N}";
        if (ObjectType != "instagram" || Entry is null || Entry.Length > 50)
        {
            throw InvalidEvent();
        }

        List<SimulatorInboundText> texts = [];
        int remaining = 50;
        foreach (var entry in Entry)
        {
            if (entry?.Id != account || entry.Messaging is null || entry.Messaging.Length > remaining)
            {
                throw InvalidEvent();
            }

            remaining -= entry.Messaging.Length;
            foreach (var item in entry.Messaging)
            {
                if (item is null)
                {
                    throw InvalidEvent();
                }

                // Echoes, unsent messages and non-text notifications never start a customer turn.
                if (item.Message is not { Text: not null, IsEcho: false, IsDeleted: false, IsUnsupported: false } dm)
                {
                    continue;
                }

                if (item.Recipient?.Id != account || string.IsNullOrWhiteSpace(item.Sender?.Id) || item.Sender.Id == account)
                {
                    throw InvalidEvent();
                }

                texts.Add(new SimulatorInboundText(item.Sender.Id, dm.Mid, dm.Text));
            }
        }

        return texts;
    }

    private static DomainValidationException InvalidEvent() =>
        new("Provide Instagram Direct events for the selected simulated professional account.")
        { Code = "instagram.simulator.invalid_event" };
}

internal sealed record InstagramAccountEntry(string? Id, InstagramMessagingEvent?[]? Messaging);
internal sealed record InstagramMessagingEvent(InstagramParty? Sender, InstagramParty? Recipient, InstagramDirectMessage? Message);
internal sealed record InstagramParty(string? Id);
internal sealed record InstagramDirectMessage(string? Mid, string? Text,
    [property: JsonPropertyName("is_echo")] bool IsEcho, [property: JsonPropertyName("is_deleted")] bool IsDeleted,
    [property: JsonPropertyName("is_unsupported")] bool IsUnsupported);
