using System.Globalization;

namespace OmniAI.Domain.Conversations;

/// <summary>
/// Identity of a <see cref="Message"/>.
/// </summary>
/// <remarks>
/// Follows the accepted <c>TenantId</c> design. UUID version 7 is a particularly
/// good fit here: message identifiers are generated in volume and, being
/// time-ordered, they sort in creation order without a separate sequence column.
/// </remarks>
public readonly record struct MessageId
{
    private MessageId(Guid value) => Value = value;

    /// <summary>The underlying identifier.</summary>
    public Guid Value { get; }

    /// <summary>
    /// True for the uninitialised <c>default</c> value. See
    /// <see cref="ConversationId.IsEmpty"/> for the reasoning.
    /// </summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Generates a new identity.</summary>
    public static MessageId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing identifier.</summary>
    /// <exception cref="DomainValidationException">The identifier is empty.</exception>
    public static MessageId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Message id must not be an empty GUID.")
            {
                Code = "conversations.message_id.invalid",
            };
        }

        return new MessageId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
