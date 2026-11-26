using System.Text.Json.Serialization;
using OmniAI.Application.Conversations;
using OmniAI.Domain;

namespace OmniAI.Api.Conversations;

// A Page event fixture for the authenticated development simulator.
internal sealed record MessengerPageEnvelope(
    [property: JsonPropertyName("object")] string? ObjectType, MessengerPageEntry?[]? Entry)
{
    public List<SimulatorInboundText> ReadTexts(Guid pageId)
    {
        string page = pageId.ToString("N");
        if (ObjectType != "page" || Entry is null || Entry.Length > 50)
        {
            throw InvalidEnvelope();
        }

        List<SimulatorInboundText> result = [];
        int events = 0;
        foreach (var entry in Entry)
        {
            if (entry?.Id != page || entry.Messaging is null || entry.Messaging.Length > 50 - events)
            {
                throw InvalidEnvelope();
            }

            events += entry.Messaging.Length;
            foreach (var item in entry.Messaging)
            {
                if (item is null)
                {
                    throw InvalidEnvelope();
                }

                if (item.Message is not { IsEcho: false } message ||
                    (message.Text is null && message.Attachments is not { Length: > 0 }))
                {
                    continue;
                }

                if (item.Recipient?.Id != page || string.IsNullOrWhiteSpace(item.Sender?.Id) ||
                    item.Sender.Id == page || message.Attachments is { Length: > SimulatorAttachment.MaxPerMessage })
                {
                    throw InvalidEnvelope();
                }

                result.Add(new SimulatorInboundText(item.Sender.Id, message.Mid, message.Text, SimulatorAttachment.Read(message.Attachments)));
            }
        }

        return result;
    }

    private static DomainValidationException InvalidEnvelope() =>
        new("Provide Page events addressed to the selected simulated Page.")
        { Code = "messenger.simulator.invalid_page_event" };
}

internal sealed record MessengerPageEntry(string? Id, MessengerPageEvent?[]? Messaging);
internal sealed record MessengerPageEvent(MessengerParty? Sender, MessengerParty? Recipient, MessengerPageMessage? Message);
internal sealed record MessengerParty(string? Id);
internal sealed record MessengerPageMessage(string? Mid, string? Text, [property: JsonPropertyName("is_echo")] bool IsEcho,
    SimulatorAttachment?[]? Attachments = null);
