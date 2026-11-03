using System.Globalization;

namespace OmniAI.Domain.AiAgents;

/// <summary>
/// Identity of an <see cref="AiAgent"/>.
/// </summary>
/// <remarks>
/// Follows the accepted <c>TenantId</c> and <c>ConversationId</c> design: a
/// distinct type so an agent identity cannot be silently interchanged with any
/// other identifier, and UUID version 7 for new values so generated keys stay
/// time-ordered and insert locally in an index.
/// <para>
/// Written out rather than derived from a generic <c>StronglyTypedId&lt;T&gt;</c>
/// base. Four hand-written members per id type is less machinery than a generic
/// framework with source generators or interface constraints, and it keeps each
/// id's validation message specific to the thing it identifies.
/// </para>
/// </remarks>
public readonly record struct AiAgentId
{
    private AiAgentId(Guid value) => Value = value;

    /// <summary>The underlying identifier.</summary>
    public Guid Value { get; }

    /// <summary>
    /// True for the uninitialised <c>default</c> value. No factory can produce an
    /// empty identifier, but C# always permits <c>default(AiAgentId)</c> for a
    /// struct, so boundaries accepting an identity from outside the Domain should
    /// reject an empty one explicitly.
    /// </summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Generates a new identity.</summary>
    public static AiAgentId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing identifier.</summary>
    /// <exception cref="DomainValidationException">The identifier is empty.</exception>
    public static AiAgentId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("AI agent id must not be an empty GUID.")
            {
                Code = "ai-agents.id.invalid",
            };
        }

        return new AiAgentId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
