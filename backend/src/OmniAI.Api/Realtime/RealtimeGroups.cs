using System.Globalization;

namespace OmniAI.Api.Realtime;

/// <summary>
/// The names of the fan-out groups, in one place.
/// </summary>
/// <remarks>
/// A group name is a security boundary in SignalR: whoever is in a group receives
/// what is sent to it, and nothing else decides that. Building the names by
/// string concatenation at each call site is how a typo becomes a workspace that
/// receives another workspace's events — or, more likely, one that silently
/// receives nothing. They are built once, here, from typed values.
/// <para>
/// The prefixes matter too. Without them an admin group named by a workspace id
/// and a visitor group named by a conversation id are both bare GUIDs in the same
/// namespace, and the only thing stopping one from colliding with the other is
/// that GUIDs rarely collide — which is not a reason, it is a hope.
/// </para>
/// </remarks>
internal static class RealtimeGroups
{
    /// <summary>The staff of one workspace.</summary>
    internal static string ForTenant(Guid tenantId) =>
        string.Create(CultureInfo.InvariantCulture, $"tenant:{tenantId:D}");

    /// <summary>The visitor having one conversation.</summary>
    internal static string ForConversation(Guid conversationId) =>
        string.Create(CultureInfo.InvariantCulture, $"conversation:{conversationId:D}");
}

/// <summary>
/// The one message this platform pushes, and its name.
/// </summary>
/// <remarks>
/// Named once so the hub, the notifier and the TypeScript client cannot drift
/// apart silently: a renamed method on one side is a client that stops updating
/// with no error anywhere.
/// </remarks>
internal static class RealtimeEvents
{
    /// <summary>
    /// "This conversation is not what you last read."
    /// </summary>
    /// <remarks>
    /// The payload is the conversation id and nothing else. See
    /// <c>IConversationRealtimeNotifier</c> for why that is the whole design
    /// rather than an optimisation that was never made.
    /// </remarks>
    internal const string ConversationChanged = "conversationChanged";
}
