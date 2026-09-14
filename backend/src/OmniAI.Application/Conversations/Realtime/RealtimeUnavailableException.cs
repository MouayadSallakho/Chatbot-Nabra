namespace OmniAI.Application.Conversations.Realtime;

/// <summary>
/// A realtime ticket was asked for on a deployment that cannot mint one.
/// </summary>
/// <remarks>
/// Reported as a service problem rather than a caller's mistake, because it is
/// one: the request was correct and fully authorised, and the platform is
/// missing a dependency. The visitor's chat keeps working without live updates,
/// so the client treats this as "no live updates here" and not as a failure of
/// the conversation.
/// </remarks>
public sealed class RealtimeUnavailableException()
    : Exception(
        "Live updates are not available on this deployment. Messages are still "
        + "sent and received normally.");
