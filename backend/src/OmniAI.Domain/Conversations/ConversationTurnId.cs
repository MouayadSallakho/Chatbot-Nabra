using System.Globalization;

namespace OmniAI.Domain.Conversations;

/// <summary>
/// The identity of one conversation turn.
/// </summary>
/// <remarks>
/// A UUIDv7 like every other identifier in this model. It orders nothing: a
/// turn's place in a conversation comes from the ingestion sequence of the
/// messages it produced, never from this value.
/// </remarks>
public readonly record struct ConversationTurnId
{
    private ConversationTurnId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static ConversationTurnId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">The value is empty.</exception>
    public static ConversationTurnId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Conversation turn id must not be an empty GUID.");
        }

        return new ConversationTurnId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
