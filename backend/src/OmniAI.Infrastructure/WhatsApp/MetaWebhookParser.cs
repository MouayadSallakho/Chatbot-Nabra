using System.Globalization;
using System.Text.Json;
using OmniAI.Application.WhatsApp;

namespace OmniAI.Infrastructure.WhatsApp;

/// <summary>
/// Reads Meta's webhook envelope into the facts the platform acts on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every Meta-specific fact in the inbound direction is in this
/// file.</strong> Entries, changes, values, metadata, the shape of a text body,
/// the spelling of a delivery status. Above this, a webhook is a list of messages
/// and a list of statuses.
/// </para>
/// <para>
/// <strong>Written defensively throughout, and never throws.</strong> The input
/// is a document from another company that can change without notice and, more
/// to the point, one that has been signed but not vouched for: a valid signature
/// says the bytes came from Meta, not that they contain what this code expects.
/// Anything unrecognised is skipped rather than raised, because the alternative
/// is answering with a failure and being sent the same payload again for hours.
/// </para>
/// <para>
/// <strong>Only the fields named here are read.</strong> Meta also sends the
/// customer's profile name, their locale and a contacts array; none is taken.
/// What is not read cannot be stored, and what is not stored cannot leak.
/// </para>
/// </remarks>
internal sealed class MetaWebhookParser : IWhatsAppWebhookParser
{
    /// <summary>
    /// A ceiling on how much one payload may ask the platform to do.
    /// </summary>
    /// <remarks>
    /// Meta batches, so a webhook carrying several messages is ordinary. A body
    /// claiming thousands is not, and a parser that obliges would turn one
    /// request into thousands of database writes inside a window Meta expects an
    /// answer in.
    /// </remarks>
    private const int MaxItems = 200;

    public WhatsAppWebhook Parse(ReadOnlySpan<byte> rawBody)
    {
        if (rawBody.IsEmpty)
        {
            return WhatsAppWebhook.Empty;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(rawBody.ToArray());
        }
        catch (JsonException)
        {
            // Signed, and not JSON. Nothing to act on, and nothing a retry would
            // improve.
            return WhatsAppWebhook.Empty;
        }

        using (document)
        {
            List<InboundWebhookMessage> messages = [];
            List<InboundWebhookStatus> statuses = [];

            foreach (JsonElement value in Values(document.RootElement))
            {
                string? phoneNumberId = Text(Child(Child(value, "metadata"), "phone_number_id"));

                if (string.IsNullOrWhiteSpace(phoneNumberId))
                {
                    // <strong>The routing key, and the only one.</strong> Without
                    // it there is no honest way to decide whose conversation this
                    // is: every other workspace-shaped field in the body is one an
                    // attacker could have chosen.
                    continue;
                }

                ReadMessages(value, phoneNumberId, messages);
                ReadStatuses(value, statuses);
            }

            return messages.Count == 0 && statuses.Count == 0
                ? WhatsAppWebhook.Empty
                : new WhatsAppWebhook(messages, statuses);
        }
    }

    /// <summary>The <c>value</c> object of every change, across every entry.</summary>
    private static IEnumerable<JsonElement> Values(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (JsonElement entry in Array(root, "entry"))
        {
            foreach (JsonElement change in Array(entry, "changes"))
            {
                if (Child(change, "value") is JsonElement value
                    && value.ValueKind == JsonValueKind.Object)
                {
                    yield return value;
                }
            }
        }
    }

    private static void ReadMessages(
        JsonElement value,
        string phoneNumberId,
        List<InboundWebhookMessage> into)
    {
        foreach (JsonElement message in Array(value, "messages"))
        {
            if (into.Count >= MaxItems)
            {
                return;
            }

            string? id = Text(Child(message, "id"));
            string? from = Text(Child(message, "from"));
            string? type = Text(Child(message, "type"));

            if (string.IsNullOrWhiteSpace(id)
                || string.IsNullOrWhiteSpace(from)
                || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            // The body is read only for a text message. Every other type is
            // carried through with its type and no body at all, so nothing
            // downstream can mistake a caption or a filename for something the
            // customer typed.
            string? text = type == "text"
                ? Text(Child(Child(message, "text"), "body"))
                : null;

            into.Add(new InboundWebhookMessage(
                phoneNumberId,
                id,
                from,
                type,
                text,
                Instant(Child(message, "timestamp"))));
        }
    }

    private static void ReadStatuses(JsonElement value, List<InboundWebhookStatus> into)
    {
        foreach (JsonElement status in Array(value, "statuses"))
        {
            if (into.Count >= MaxItems)
            {
                return;
            }

            string? id = Text(Child(status, "id"));
            string? state = Text(Child(status, "status"));

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(state))
            {
                continue;
            }

            into.Add(new InboundWebhookStatus(
                id,
                state,
                Reason(status),
                Instant(Child(status, "timestamp"))));
        }
    }

    /// <summary>
    /// Why a delivery failed, in Meta's words, when it said.
    /// </summary>
    /// <remarks>
    /// The error <c>title</c> only. It is a short phrase written for a person
    /// ("Message undeliverable"), unlike <c>message</c> and <c>details</c>, which
    /// can echo request content. This string is stored and shown in the inbox.
    /// </remarks>
    private static string? Reason(JsonElement status)
    {
        foreach (JsonElement error in Array(status, "errors"))
        {
            if (Text(Child(error, "title")) is string title && !string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }

        return null;
    }

    /// <summary>
    /// A Unix-seconds timestamp, as a string or a number.
    /// </summary>
    /// <remarks>
    /// Meta sends it quoted; the fallback exists because a value that is merely
    /// formatted differently is not a reason to drop a customer's message. An
    /// unreadable one becomes <see cref="DateTimeOffset.UnixEpoch"/> rather than
    /// "now", so a timestamp the platform did not receive is visibly wrong rather
    /// than plausibly wrong.
    /// </remarks>
    private static DateTimeOffset Instant(JsonElement? element)
    {
        long? seconds = element switch
        {
            { ValueKind: JsonValueKind.String } text
                when long.TryParse(
                    text.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long parsed) => parsed,
            { ValueKind: JsonValueKind.Number } number
                when number.TryGetInt64(out long read) => read,
            _ => null,
        };

        return seconds is long value
            ? DateTimeOffset.FromUnixTimeSeconds(value)
            : DateTimeOffset.UnixEpoch;
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name)
    {
        if (Child(parent, name) is JsonElement child && child.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in child.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static JsonElement? Child(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } element
        && element.TryGetProperty(name, out JsonElement child)
            ? child
            : null;

    private static string? Text(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
