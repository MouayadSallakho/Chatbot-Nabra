namespace OmniAI.Domain.WebChat;

/// <summary>
/// Lifecycle state of a website chat endpoint.
/// </summary>
/// <remarks>
/// The same three states an AI agent has, and for the same reason: something
/// being configured is not the same as something serving the public, and taking
/// it out of service is not the same as never having configured it.
/// <para>
/// Only <see cref="Active"/> serves live conversations. A draft has not been
/// published yet; a disabled endpoint has been withdrawn, and its existing
/// transcripts stay readable to the visitors who own them while no new
/// conversation or turn is accepted.
/// </para>
/// There is no zero member, so an uninitialised value is detectable.
/// </remarks>
public enum WebChatEndpointStatus
{
    /// <summary>Configured, not yet serving anybody.</summary>
    Draft = 1,

    /// <summary>Live: the hosted page accepts visitors.</summary>
    Active = 2,

    /// <summary>Withdrawn from service.</summary>
    Disabled = 3,
}
