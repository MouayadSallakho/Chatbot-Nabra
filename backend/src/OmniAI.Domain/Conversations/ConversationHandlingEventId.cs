using System.Globalization;

namespace OmniAI.Domain.Conversations;

/// <summary>
/// The identity of one change of who answers a conversation.
/// </summary>
/// <remarks>
/// A UUIDv7 like every other identifier in this model. It orders nothing: the
/// event's place in the transcript is <see cref="ConversationHandlingEvent.AfterSequence"/>.
/// </remarks>
public readonly record struct ConversationHandlingEventId
{
    private ConversationHandlingEventId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static ConversationHandlingEventId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">The value is empty.</exception>
    public static ConversationHandlingEventId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Handling event id must not be an empty GUID.")
            {
                Code = "conversations.handling_event.invalid",
            };
        }

        return new ConversationHandlingEventId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
