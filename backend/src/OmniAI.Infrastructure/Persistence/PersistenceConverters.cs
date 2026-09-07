using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Domain.WebChat;

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

    public static readonly ValueConverter<TenantRoleId, Guid> TenantRoleId =
        new(id => id.Value, value => Domain.Authorization.TenantRoleId.From(value));

    public static readonly ValueConverter<TenantInvitationId, Guid> TenantInvitationId =
        new(id => id.Value, value => Domain.Invitations.TenantInvitationId.From(value));

    /// <summary>
    /// An optional user id, for the account that accepted an invitation.
    /// </summary>
    /// <remarks>
    /// A separate converter rather than a nullable use of the one above, because
    /// EF Core matches converters by exact type and would otherwise store the
    /// default struct as an empty GUID -- which the factory refuses to read back.
    /// </remarks>
    public static readonly ValueConverter<UserId?, Guid?> NullableUserId =
        new(
            id => id == null ? null : id.Value.Value,
            value => value == null ? null : Domain.Users.UserId.From(value.Value));

    /// <summary>
    /// An email address, stored as it was written.
    /// </summary>
    /// <remarks>
    /// The canonical form travels beside it in its own shadow column rather than
    /// being re-derived on read: see <c>EmailAddress.FromStored</c> for why a row
    /// keeps the form it was matched on.
    /// </remarks>
    public static readonly ValueConverter<EmailAddress, string> EmailAddress =
        new(email => email.Value, value => Domain.Users.EmailAddress.Create(value));

    /// <summary>
    /// The digest of an invitation token, stored as raw bytes.
    /// </summary>
    /// <remarks>
    /// <c>bytea</c> rather than text. There is deliberately no converter anywhere
    /// in this file that could store an invitation's raw token, because no type in
    /// the model holds one.
    /// </remarks>
    public static readonly ValueConverter<InvitationTokenHash, byte[]> InvitationTokenHash =
        new(hash => hash.ToArray(), value => Domain.Invitations.InvitationTokenHash.From(value));

    /// <summary>
    /// A role name, stored as the display form the workspace typed.
    /// </summary>
    /// <remarks>
    /// The normalised form the unique index is built on is <em>not</em> produced
    /// by this converter, because one converter cannot fill two columns. It is a
    /// separate column that the role repository sets from
    /// <c>TenantRoleName.Normalised</c> -- see TenantRoleConfiguration for why
    /// that is preferred to a database-computed column.
    /// </remarks>
    public static readonly ValueConverter<TenantRoleName, string> TenantRoleName =
        new(name => name.Value, value => Domain.Authorization.TenantRoleName.Create(value));

    public static readonly ValueConverter<PermissionCode, string> PermissionCode =
        new(code => code.Value, value => Domain.Authorization.PermissionCode.Create(value));

    public static readonly ValueConverter<ConversationId, Guid> ConversationId =
        new(id => id.Value, value => Domain.Conversations.ConversationId.From(value));

    public static readonly ValueConverter<MessageId, Guid> MessageId =
        new(id => id.Value, value => Domain.Conversations.MessageId.From(value));

    public static readonly ValueConverter<WebChatEndpointId, Guid> WebChatEndpointId =
        new(id => id.Value, value => Domain.WebChat.WebChatEndpointId.From(value));

    public static readonly ValueConverter<ConversationTurnId, Guid> ConversationTurnId =
        new(id => id.Value, value => Domain.Conversations.ConversationTurnId.From(value));

    /// <summary>
    /// An optional message id, for a turn that has no reply yet.
    /// </summary>
    /// <remarks>
    /// A separate converter rather than a nullable use of the one above, because
    /// EF Core matches converters by exact type and would otherwise store the
    /// default struct as an empty GUID -- which the factory refuses to read back.
    /// </remarks>
    public static readonly ValueConverter<MessageId?, Guid?> NullableMessageId =
        new(
            id => id == null ? null : id.Value.Value,
            value => value == null ? null : Domain.Conversations.MessageId.From(value.Value));

    /// <summary>
    /// A website chat public key, stored as the text it is published as.
    /// </summary>
    /// <remarks>
    /// In the clear, deliberately. It appears in every hosted chat URL, so a
    /// digest would protect nothing and would only stop the public lookup being an
    /// index seek on the value itself.
    /// </remarks>
    public static readonly ValueConverter<WebChatPublicKey, string> WebChatPublicKey =
        new(key => key.Value, value => Domain.WebChat.WebChatPublicKey.Create(value));

    /// <summary>
    /// A client's idempotency key, stored as the text it sent.
    /// </summary>
    public static readonly ValueConverter<ClientMessageId, string> ClientMessageId =
        new(id => id.Value, value => Domain.Conversations.ClientMessageId.Create(value));

    /// <summary>
    /// The digest of an anonymous conversation credential, stored as raw bytes.
    /// </summary>
    /// <remarks>
    /// <c>bytea</c> rather than text. There is deliberately no converter anywhere
    /// in this file that could store a raw conversation secret, because no type in
    /// the model holds one.
    /// </remarks>
    public static readonly ValueConverter<WebChatAccessTokenHash, byte[]> WebChatAccessTokenHash =
        new(hash => hash.ToArray(), value => Domain.WebChat.WebChatAccessTokenHash.From(value));

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
