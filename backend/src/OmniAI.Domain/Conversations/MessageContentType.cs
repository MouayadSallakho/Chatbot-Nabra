namespace OmniAI.Domain.Conversations;

/// <summary>
/// The kind of content a message carries.
/// </summary>
/// <remarks>
/// There is no zero member, so an uninitialised value is detectable.
/// </remarks>
public enum MessageContentType
{
    /// <summary>Free text written by a customer, an AI agent or a human agent.</summary>
    Text = 1,

    /// <summary>A platform-authored note recording something that happened.</summary>
    System = 2,

    /// <summary>Customer media with an optional caption.</summary>
    Media = 3,
}
