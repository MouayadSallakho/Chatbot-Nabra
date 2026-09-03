namespace OmniAI.Domain.Conversations;

/// <summary>
/// The kind of content a message carries.
/// </summary>
/// <remarks>
/// Two members, because two are all that is justified today. Image, video, audio,
/// document, location, contact, interactive and template content arrive with the
/// channel and media requirements that define them -- and each will bring its own
/// validation rules, which is precisely why a polymorphic content hierarchy is
/// not being built in advance of knowing them.
/// There is no zero member, so an uninitialised value is detectable.
/// </remarks>
public enum MessageContentType
{
    /// <summary>Free text written by a customer, an AI agent or a human agent.</summary>
    Text = 1,

    /// <summary>A platform-authored note recording something that happened.</summary>
    System = 2,
}
