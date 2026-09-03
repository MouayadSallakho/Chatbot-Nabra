namespace OmniAI.Domain.Conversations;

/// <summary>
/// The kind of communication channel a conversation takes place on, expressed at
/// business level.
/// </summary>
/// <remarks>
/// The Domain is allowed to know *that* a conversation is a WhatsApp conversation,
/// because that changes how the business reasons about it. It is not allowed to
/// know anything about *how* WhatsApp works: no endpoints, phone number ids, page
/// ids, access tokens, webhook secrets, provider DTOs or SDK types appear here or
/// anywhere else in the Domain. Those belong to channel adapters in Infrastructure.
/// <para>
/// One enum rather than an aggregate per channel: the channels differ in
/// transport, not in what a conversation *is*.
/// </para>
/// There is no zero member, so an uninitialised value is detectable.
/// </remarks>
public enum ChannelKind
{
    /// <summary>Chat widget embedded in the tenant's own website.</summary>
    WebChat = 1,

    /// <summary>WhatsApp.</summary>
    WhatsApp = 2,

    /// <summary>Instagram direct messages.</summary>
    Instagram = 3,

    /// <summary>Facebook Messenger.</summary>
    Messenger = 4,
}
