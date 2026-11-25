using System.Text.Json.Serialization;
using OmniAI.Application.Conversations;
using OmniAI.Domain;

namespace OmniAI.Api.Conversations;

// One ownership notification per local fixture. These app IDs are synthetic.
internal sealed record InstagramHandoverEnvelope(
    [property: JsonPropertyName("object")] string? ObjectType, InstagramHandoverEntry?[]? Entry)
{
    internal const string NabraApp = "10001";

    public InstagramHandover Read(Guid agent)
    {
        string account = $"ig-account-{agent:N}";
        if (ObjectType != "instagram" || Entry is not [{ } entry] || entry.Id != account ||
            entry.Changes is not [{ Field: "messaging_handover", Value: { } value }] ||
            value.Recipient?.Id != account || string.IsNullOrWhiteSpace(value.Sender?.Id) ||
            value.Timestamp is not (> 0 and <= 253402300799999L) ||
            (value.Pass is null) == (value.Take is null))
        {
            throw InvalidEvent();
        }

        var owner = (value.Pass ?? value.Take)!.NewOwnerAppId;
        if (string.IsNullOrEmpty(owner) || owner.Length > 32 || !owner.All(char.IsAsciiDigit))
        {
            throw InvalidEvent();
        }
        return new InstagramHandover(value.Sender.Id, owner != NabraApp, value.Timestamp.Value);
    }

    private static DomainValidationException InvalidEvent() =>
        new("Provide one timestamped Instagram pass or take notification for the selected test account.")
        { Code = "instagram.simulator.invalid_handover" };
}

internal sealed record InstagramHandoverEntry(string? Id, InstagramHandoverChange?[]? Changes);
internal sealed record InstagramHandoverChange(string? Field, InstagramHandoverValue? Value);
internal sealed record InstagramHandoverValue(InstagramParty? Sender, InstagramParty? Recipient, long? Timestamp,
    [property: JsonPropertyName("pass_thread_control")] InstagramHandoverTransfer? Pass,
    [property: JsonPropertyName("take_thread_control")] InstagramHandoverTransfer? Take);
internal sealed record InstagramHandoverTransfer([property: JsonPropertyName("new_owner_app_id")] string? NewOwnerAppId);
