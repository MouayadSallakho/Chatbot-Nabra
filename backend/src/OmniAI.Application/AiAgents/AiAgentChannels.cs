using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.AiAgents;

/// <summary>Which channel an agent answers on.</summary>
public enum AiAgentChannelKind
{
    /// <summary>A website chat link (hosted page and embedded widget).</summary>
    WebChat = 1,

    /// <summary>A WhatsApp connection.</summary>
    WhatsApp = 2,
}

/// <summary>
/// The channel kinds a reader is allowed to see on an agent.
/// </summary>
/// <remarks>
/// A flag set rather than two booleans so that "neither" is a value the reader
/// can be handed without a query being made at all.
/// </remarks>
[Flags]
public enum AiAgentChannelKinds
{
    None = 0,
    WebChat = 1,
    WhatsApp = 2,
}

/// <summary>
/// One channel an agent answers on, as an agent read publishes it (Phase 37.5a,
/// row 18).
/// </summary>
/// <param name="Id">The website chat endpoint's or the WhatsApp connection's id.</param>
/// <param name="Status">That channel's own status: <c>Draft</c>, <c>Active</c> or <c>Disabled</c>.</param>
/// <remarks>
/// Identity and status only. No public key, number, allowlist or credential: the
/// channel's own read, behind its own permission, is where those live.
/// </remarks>
public sealed record AiAgentChannelView(AiAgentChannelKind Kind, Guid Id, string Status);

/// <summary>
/// Reads which channels a page of agents answers on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>At most one statement per kind, whatever the page size.</strong> The
/// agents are named together, so a list of twenty agents costs the same as one.
/// A kind the reader may not see is never queried.
/// </para>
/// <para>
/// Scoped by the workspace as well as the agents, so an agent id from another
/// workspace finds nothing.
/// </para>
/// </remarks>
public interface IAiAgentChannelReader
{
    /// <returns>
    /// Each agent's channels, web chat first, then WhatsApp, each in creation
    /// order. An agent with no visible channel is absent from the map.
    /// </returns>
    Task<IReadOnlyDictionary<AiAgentId, IReadOnlyList<AiAgentChannelView>>> ListAsync(
        TenantId tenantId,
        IReadOnlyCollection<AiAgentId> agentIds,
        AiAgentChannelKinds kinds,
        CancellationToken cancellationToken = default);
}

/// <summary>Decides which channel kinds a reader may see.</summary>
internal static class AiAgentChannelVisibility
{
    /// <summary>
    /// The kinds whose own view permission the reader holds. Checked once per
    /// request, from the permissions the agent read already resolved.
    /// </summary>
    public static AiAgentChannelKinds For(IReadOnlyCollection<PermissionCode> held)
    {
        ArgumentNullException.ThrowIfNull(held);

        AiAgentChannelKinds kinds = AiAgentChannelKinds.None;

        if (held.Contains(PlatformPermissions.WebsiteChatView))
        {
            kinds |= AiAgentChannelKinds.WebChat;
        }

        if (held.Contains(PlatformPermissions.WhatsAppView))
        {
            kinds |= AiAgentChannelKinds.WhatsApp;
        }

        return kinds;
    }
}
