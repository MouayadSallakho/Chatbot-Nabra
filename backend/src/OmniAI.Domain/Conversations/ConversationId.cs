using System.Globalization;

namespace OmniAI.Domain.Conversations;

/// <summary>
/// Identity of a <see cref="Conversation"/>.
/// </summary>
/// <remarks>
/// Follows the accepted <c>TenantId</c> design: a distinct type so a conversation
/// identity cannot be silently interchanged with any other identifier, and
/// UUID version 7 for new values so generated keys stay time-ordered and insert
/// locally in an index.
/// </remarks>
public readonly record struct ConversationId
{
    private ConversationId(Guid value) => Value = value;

    /// <summary>The underlying identifier.</summary>
    public Guid Value { get; }

    /// <summary>
    /// True for the uninitialised <c>default</c> value. No factory can produce an
    /// empty identifier, but C# always permits <c>default(ConversationId)</c> for
    /// a struct, so boundaries accepting an identity from outside the Domain
    /// should reject an empty one explicitly.
    /// </summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Generates a new identity.</summary>
    public static ConversationId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing identifier.</summary>
    /// <exception cref="DomainValidationException">The identifier is empty.</exception>
    public static ConversationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Conversation id must not be an empty GUID.");
        }

        return new ConversationId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
