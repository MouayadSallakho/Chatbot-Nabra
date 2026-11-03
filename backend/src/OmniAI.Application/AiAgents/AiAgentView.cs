using OmniAI.Domain.AiAgents;

namespace OmniAI.Application.AiAgents;

/// <summary>
/// The AI agent information a use case hands back to its caller.
/// </summary>
/// <param name="Id">The agent's identity.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="Name">The canonical display name, as the Domain normalised it.</param>
/// <param name="Instructions">The canonical instruction text, formatting preserved.</param>
/// <param name="Status">The agent's lifecycle state.</param>
/// <param name="CreatedAt">When the agent was created, in UTC.</param>
/// <param name="UpdatedAt">When the agent last actually changed, in UTC.</param>
/// <remarks>
/// A read model, not the aggregate. Handing <c>AiAgent</c> itself to a caller
/// would expose its behaviour -- <c>Rename</c>, <c>Activate</c>, <c>Disable</c> --
/// to layers that have no business invoking it, and would make the HTTP contract
/// move whenever the aggregate does.
/// <para>
/// It carries the seven fields the Domain owns today and nothing else. In
/// particular there is no model name, no provider id, no temperature, no token
/// limit, no external assistant id and no persistence metadata: the aggregate has
/// none of those, and this type must not be where they quietly appear.
/// <c>Status</c> stays the Domain enum, because choosing how a status looks on
/// the wire is the API's decision rather than the Application's.
/// </para>
/// </remarks>
public sealed record AiAgentView(
    Guid Id,
    Guid TenantId,
    string Name,
    string Instructions,
    AiAgentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects an aggregate into its read model.</summary>
    internal static AiAgentView From(AiAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return new AiAgentView(
            agent.Id.Value,
            agent.TenantId.Value,
            agent.Name.Value,
            agent.Instructions.Value,
            agent.Status,
            agent.CreatedAt,
            agent.UpdatedAt);
    }

    /// <summary>
    /// The channels this agent answers on that the reader may see, filled only by
    /// the single-agent read (Phase 37.5a, row 18). A write's response leaves it
    /// empty, so the API publishes it on the read and nowhere else.
    /// </summary>
    public IReadOnlyList<AiAgentChannelView> Channels { get; init; } = [];
}
