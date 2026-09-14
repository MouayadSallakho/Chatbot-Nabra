using System.Globalization;

namespace OmniAI.Domain.Conversations;

/// <summary>
/// The identity of one reply sent by a person.
/// </summary>
/// <remarks>
/// A UUIDv7 like every other identifier in this model. It orders nothing: the
/// reply's place in the transcript comes from the ingestion sequence of the
/// message it produced, never from this value.
/// </remarks>
public readonly record struct HumanReplyId
{
    private HumanReplyId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static HumanReplyId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">The value is empty.</exception>
    public static HumanReplyId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Human reply id must not be an empty GUID.");
        }

        return new HumanReplyId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
