using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Invitations;
using OmniAI.Domain.Knowledge;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Domain.WebChat;
using OmniAI.Domain.WhatsApp;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Explicit conversions between Domain types and their relational representation.
/// </summary>
/// <remarks>
/// Written out by hand rather than produced by a reflection-driven convention:
/// each is two lines, and being able to read exactly how a column is stored is
/// worth more here than saving the repetition. Nothing in this file is visible to
/// the Domain.
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

    public static readonly ValueConverter<CustomerId, Guid> CustomerId =
        new(id => id.Value, value => Domain.Customers.CustomerId.From(value));

    /// <summary>
    /// The merge pointer, which is absent for every canonical customer.
    /// </summary>
    /// <remarks>
    /// Null is the ordinary case here, not an error case: most customers have
    /// never been merged into anything.
    /// </remarks>
    public static readonly ValueConverter<CustomerId?, Guid?> NullableCustomerId =
        new(
            id => id == null ? null : id.Value.Value,
            value => value == null ? null : Domain.Customers.CustomerId.From(value.Value));

    /// <summary>
    /// The optional customer label.
    /// </summary>
    /// <remarks>
    /// Blank and absent are the same thing, so a row written with an empty string
    /// by some future path still reads back as "no label" rather than as a label
    /// nobody can see.
    /// </remarks>
    public static readonly ValueConverter<CustomerDisplayName?, string?> NullableCustomerDisplayName =
        new(
            name => name == null ? null : name.Value,
            value => Domain.Customers.CustomerDisplayName.CreateOrNull(value));

    public static readonly ValueConverter<MessageId, Guid> MessageId =
        new(id => id.Value, value => Domain.Conversations.MessageId.From(value));

    public static readonly ValueConverter<WebChatEndpointId, Guid> WebChatEndpointId =
        new(id => id.Value, value => Domain.WebChat.WebChatEndpointId.From(value));

    public static readonly ValueConverter<ConversationTurnId, Guid> ConversationTurnId =
        new(id => id.Value, value => Domain.Conversations.ConversationTurnId.From(value));

    public static readonly ValueConverter<WhatsAppConnectionId, Guid> WhatsAppConnectionId =
        new(id => id.Value, value => Domain.WhatsApp.WhatsAppConnectionId.From(value));

    public static readonly ValueConverter<WhatsAppContactId, Guid> WhatsAppContactId =
        new(id => id.Value, value => Domain.WhatsApp.WhatsAppContactId.From(value));

    public static readonly ValueConverter<InboundWhatsAppMessageId, Guid> InboundWhatsAppMessageId =
        new(id => id.Value, value => Domain.WhatsApp.InboundWhatsAppMessageId.From(value));

    public static readonly ValueConverter<WhatsAppDeliveryId, Guid> WhatsAppDeliveryId =
        new(id => id.Value, value => Domain.WhatsApp.WhatsAppDeliveryId.From(value));

    public static readonly ValueConverter<WabaId, string> WabaId =
        new(id => id.Value, value => Domain.WhatsApp.WabaId.Create(value));

    public static readonly ValueConverter<PhoneNumberId, string> PhoneNumberId =
        new(id => id.Value, value => Domain.WhatsApp.PhoneNumberId.Create(value));

    public static readonly ValueConverter<WhatsAppCustomerId, string> WhatsAppCustomerId =
        new(id => id.Value, value => Domain.WhatsApp.WhatsAppCustomerId.Create(value));

    public static readonly ValueConverter<ProviderMessageId, string> ProviderMessageId =
        new(id => id.Value, value => Domain.WhatsApp.ProviderMessageId.Create(value));

    /// <remarks>
    /// Nullable, because a delivery has no provider id until the provider has
    /// accepted it -- and never will if it never did.
    /// </remarks>
    public static readonly ValueConverter<ProviderMessageId?, string?> NullableProviderMessageId =
        new(
            id => id == null ? null : id.Value.Value,
            value => value == null ? null : Domain.WhatsApp.ProviderMessageId.Create(value));

    public static readonly ValueConverter<HumanReplyId, Guid> HumanReplyId =
        new(id => id.Value, value => Domain.Conversations.HumanReplyId.From(value));

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

    public static readonly ValueConverter<WebChatOrigin, string> WebChatOrigin =
        new(origin => origin.Value, value => Domain.WebChat.WebChatOrigin.Create(value));

    // Nullable on both sides: a hosted session has no embedding website, and null
    // is what "not embedded anywhere" means. A converter that mapped null to an
    // empty string would make the two indistinguishable.
    public static readonly ValueConverter<WebChatOrigin?, string?> NullableWebChatOrigin =
        new(
            origin => origin == null ? null : origin.Value,
            value => value == null ? null : Domain.WebChat.WebChatOrigin.Create(value));

    public static readonly ValueConverter<WebChatAccentColor, string> WebChatAccentColor =
        new(colour => colour.Value, value => Domain.WebChat.WebChatAccentColor.Create(value));

    public static readonly ValueConverter<WebChatGreeting?, string?> NullableWebChatGreeting =
        new(
            greeting => greeting == null ? null : greeting.Value,
            value => Domain.WebChat.WebChatGreeting.CreateOrNone(value));

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

    /// <summary>The identity of one platform plan.</summary>
    public static readonly ValueConverter<Domain.Plans.PlanId, Guid> PlanId =
        new(id => id.Value, value => Domain.Plans.PlanId.From(value));

    /// <summary>A plan's stable code.</summary>
    public static readonly ValueConverter<Domain.Plans.PlanCode, string> PlanCode =
        new(code => code.Value, value => Domain.Plans.PlanCode.Create(value));

    /// <summary>A plan's display name.</summary>
    public static readonly ValueConverter<Domain.Plans.PlanDisplayName, string> PlanDisplayName =
        new(name => name.Value, value => Domain.Plans.PlanDisplayName.Create(value));

    /// <summary>The identity of one accounted provider operation.</summary>
    public static readonly ValueConverter<Domain.Ai.AiProviderUsageId, Guid> AiProviderUsageId =
        new(id => id.Value, value => Domain.Ai.AiProviderUsageId.From(value));

    /// <summary>A model identifier, stored exactly as the provider spells it.</summary>
    public static readonly ValueConverter<Domain.Ai.ModelName, string> ModelName =
        new(name => name.Value, value => Domain.Ai.ModelName.Create(value));

    /// <summary>
    /// The model a provider said it used, which it may not have said.
    /// </summary>
    /// <remarks>
    /// Its own converter because absence is meaningful here: a null means the
    /// provider did not report a model, which is different from it reporting the
    /// one we asked for.
    /// </remarks>
    public static readonly ValueConverter<Domain.Ai.ModelName?, string?> NullableModelName =
        new(
            name => name == null ? null : name.Value.Value,
            value => Domain.Ai.ModelName.CreateOrNull(value));

    /// <summary>The currency a cost estimate is in. Null when unrated.</summary>
    public static readonly ValueConverter<Domain.Ai.CurrencyCode?, string?> NullableCurrencyCode =
        new(
            code => code == null ? null : code.Value.Value,
            value => value == null ? null : Domain.Ai.CurrencyCode.Create(value));

    public static readonly ValueConverter<KnowledgeSourceId, Guid> KnowledgeSourceId =
        new(id => id.Value, value => Domain.Knowledge.KnowledgeSourceId.From(value));

    /// <remarks>
    /// Display metadata, stored as ordinary text. The value object has already
    /// stripped path segments and control characters, so what reaches the column
    /// is one safe name.
    /// </remarks>
    public static readonly ValueConverter<SourceFileName, string> SourceFileName =
        new(name => name.Value, value => Domain.Knowledge.SourceFileName.Create(value));

    /// <remarks>
    /// The non-nullable form, for a source that always has a digest of the file
    /// it was read from. The nullable one below is the document's own indexed
    /// fingerprint, which is absent until something is indexed.
    /// </remarks>
    public static readonly ValueConverter<ContentFingerprint, byte[]> ContentFingerprint =
        new(
            fingerprint => fingerprint.ToArray(),
            value => Domain.Knowledge.ContentFingerprint.From(value));

    public static readonly ValueConverter<KnowledgeDocumentId, Guid> KnowledgeDocumentId =
        new(id => id.Value, value => Domain.Knowledge.KnowledgeDocumentId.From(value));

    public static readonly ValueConverter<KnowledgeTitle, string> KnowledgeTitle =
        new(title => title.Value, value => Domain.Knowledge.KnowledgeTitle.Create(value));

    public static readonly ValueConverter<KnowledgeContent, string> KnowledgeContent =
        new(content => content.Value, value => Domain.Knowledge.KnowledgeContent.Create(value));

    // Nullable on both sides: a document that has never been indexed has neither
    // a fingerprint nor a profile, and null is what "never indexed" means. A
    // converter that mapped null to an empty value would make "not indexed" and
    // "indexed as nothing" the same row.
    public static readonly ValueConverter<ContentFingerprint?, byte[]?> NullableContentFingerprint =
        new(
            fingerprint => fingerprint == null ? null : fingerprint.ToArray(),
            value => value == null ? null : Domain.Knowledge.ContentFingerprint.From(value));

    public static readonly ValueConverter<EmbeddingProfileId?, string?> NullableEmbeddingProfileId =
        new(
            profile => profile == null ? null : profile.Value,
            value => value == null ? null : Domain.Knowledge.EmbeddingProfileId.Create(value));

    public static readonly ValueConverter<Domain.Billing.BillingOfferId, Guid> BillingOfferId =
        new(id => id.Value, value => Domain.Billing.BillingOfferId.From(value));

    public static readonly ValueConverter<Domain.Billing.WorkspaceSubscriptionId, Guid>
        WorkspaceSubscriptionId =
            new(id => id.Value, value => Domain.Billing.WorkspaceSubscriptionId.From(value));

    public static readonly ValueConverter<Domain.Billing.BillingCheckoutIntentId, Guid>
        BillingCheckoutIntentId =
            new(id => id.Value, value => Domain.Billing.BillingCheckoutIntentId.From(value));

    public static readonly ValueConverter<Domain.Billing.BillingEventId, Guid> BillingEventId =
        new(id => id.Value, value => Domain.Billing.BillingEventId.From(value));

    public static readonly ValueConverter<Domain.Billing.OfferCode, string> OfferCode =
        new(code => code.Value, value => Domain.Billing.OfferCode.Create(value));

    public static readonly ValueConverter<Domain.Billing.BillingCurrency, string> BillingCurrency =
        new(currency => currency.Value, value => Domain.Billing.BillingCurrency.Create(value));

    // Opaque on purpose. Whatever a provider calls its own records is its
    // business; this platform stores the string it was handed and never parses
    // meaning out of it.
    public static readonly ValueConverter<Domain.Billing.ProviderReference, string>
        ProviderReference =
            new(
                reference => reference.Value,
                value => Domain.Billing.ProviderReference.Create(value));

    // Null means "the provider has not told us yet" -- a subscription created
    // before checkout completes has no provider id, and that is different from
    // one whose id is blank.
    public static readonly ValueConverter<Domain.Billing.ProviderReference?, string?>
        NullableProviderReference =
            new(
                reference => reference == null ? null : reference.Value.Value,
                value => Domain.Billing.ProviderReference.CreateOrNull(value));
}
