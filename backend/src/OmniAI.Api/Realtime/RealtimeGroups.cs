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

    /// <summary>
    /// One member's own notifications, in one workspace.
    /// </summary>
    /// <remarks>
    /// Keyed by workspace and member, so a signal for one member in one workspace
    /// reaches none of their colleagues and none of their other workspaces.
    /// </remarks>
    internal static string ForMember(Guid tenantId, Guid userId) =>
        string.Create(CultureInfo.InvariantCulture, $"notifications:{tenantId:D}:{userId:D}");

    /// <summary>
    /// The members of one workspace who may view its knowledge (Phase 38.7a).
    /// </summary>
    /// <remarks>
    /// Its own prefix, apart from <see cref="ForTenant"/>: joining it needs
    /// <c>knowledge.view</c>, and joining the inbox group needs
    /// <c>conversations.view</c>, so neither permission can stand in for the other.
    /// </remarks>
    internal static string ForKnowledge(Guid tenantId) =>
        string.Create(CultureInfo.InvariantCulture, $"knowledge:{tenantId:D}");
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

    /// <summary>Structural only: the payload is the workspace id, and the client re-reads.</summary>
    internal const string NotificationsChanged = "notificationsChanged";

    /// <summary>
    /// "This document's indexing started or settled" (Phase 38.7a, BA-11).
    /// </summary>
    /// <remarks>
    /// Structural only: the payload is the document id, and the client re-reads the
    /// document through the authorised HTTP read.
    /// </remarks>
    internal const string KnowledgeChanged = "knowledgeChanged";

    /// <summary>
    /// "Your read markers in this workspace moved" (Phase 39.1, mark all read).
    /// </summary>
    /// <remarks>
    /// Structural only: the payload is the workspace id. Sent to the reader's own
    /// member group, so their other tabs re-read the list; nobody else is told.
    /// </remarks>
    internal const string ConversationsRead = "conversationsRead";
}
