using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Explicit conversions between Domain types and their relational representation.
/// </summary>
/// <remarks>
/// Written out by hand rather than produced by a reflection-driven convention:
/// there are eight of them, each is two lines, and being able to read exactly how
/// a column is stored is worth more here than saving the repetition. Nothing in
/// this file is visible to the Domain.
/// </remarks>
internal static class PersistenceConverters
{
    /// <summary>Identifiers are stored as native PostgreSQL <c>uuid</c>, never as text.</summary>
    public static readonly ValueConverter<TenantId, Guid> TenantId =
        new(id => id.Value, value => Domain.Tenants.TenantId.From(value));

    public static readonly ValueConverter<UserId, Guid> UserId =
        new(id => id.Value, value => Domain.Users.UserId.From(value));

    public static readonly ValueConverter<ConversationId, Guid> ConversationId =
        new(id => id.Value, value => Domain.Conversations.ConversationId.From(value));

    public static readonly ValueConverter<MessageId, Guid> MessageId =
        new(id => id.Value, value => Domain.Conversations.MessageId.From(value));

    public static readonly ValueConverter<AiAgentId, Guid> AiAgentId =
        new(id => id.Value, value => Domain.AiAgents.AiAgentId.From(value));

    /// <summary>
    /// Scalar value objects map to plain relational columns, not JSON: they are a
    /// single string each, and a JSON column would forfeit length constraints,
    /// indexing and the unique index the slug needs.
    /// </summary>
    /// <remarks>
    /// Reading goes through the Domain factory, so a value that somehow violates
    /// a Domain rule fails loudly on load instead of silently entering the model.
    /// The factories normalise, but stored values are already canonical, so
    /// round-tripping cannot alter persisted data.
    /// </remarks>
    public static readonly ValueConverter<TenantName, string> TenantName =
        new(name => name.Value, value => Domain.Tenants.TenantName.Create(value));

    public static readonly ValueConverter<TenantSlug, string> TenantSlug =
        new(slug => slug.Value, value => Domain.Tenants.TenantSlug.Create(value));

    public static readonly ValueConverter<AiAgentName, string> AiAgentName =
        new(name => name.Value, value => Domain.AiAgents.AiAgentName.Create(value));

    /// <summary>
    /// Instruction text maps to a single <c>character varying</c> column.
    /// </summary>
    /// <remarks>
    /// Not JSON, and not a provider message array. The Domain holds one piece of
    /// text whose interior formatting is meaningful, and a relational text column
    /// stores exactly that -- byte for byte, line breaks included. Serialising it
    /// as JSON would add an encoding layer that can only lose or alter formatting,
    /// forfeit the length constraint, and encode a shape no provider has asked
    /// for.
    /// </remarks>
    public static readonly ValueConverter<AgentInstructions, string> AgentInstructions =
        new(instructions => instructions.Value, value => Domain.AiAgents.AgentInstructions.Create(value));
}
