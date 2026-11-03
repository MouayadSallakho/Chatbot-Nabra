using OmniAI.Domain.AiAgents;

namespace OmniAI.Application.AiAgents.ListAiAgents;

/// <summary>
/// One AI agent as it appears in a list.
/// </summary>
/// <param name="Id">The agent's identity.</param>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="Name">The canonical display name.</param>
/// <param name="Status">The agent's lifecycle state.</param>
/// <param name="CreatedAt">When the agent was created, in UTC.</param>
/// <param name="UpdatedAt">When the agent last actually changed, in UTC.</param>
/// <remarks>
/// <para>
/// <strong>Deliberately not <see cref="AiAgentView"/>, and the missing field is
/// the reason.</strong> Instructions can run to twenty thousand characters. A
/// page of twenty agents would carry up to four hundred thousand characters of
/// text that a list cannot show and no reader asked for -- read from disk,
/// materialised in memory, serialised, and sent over the network on every page
/// view. The projection that produces this type does not select the column at
/// all, so the cost is not paid anywhere in the stack.
/// </para>
/// <para>
/// It is also a confidentiality boundary. Instructions are the tenant's
/// configured business behaviour; a listing endpoint has no reason to publish
/// them, and not selecting them means they cannot be leaked by a later change to
/// a response shape.
/// </para>
/// <para>
/// No version marker either. A summary is not something a caller may write
/// against: conditional writes need the version of the representation the caller
/// actually read, which is the one the single-agent endpoint returns.
/// </para>
/// </remarks>
public sealed record AiAgentSummary(
    Guid Id,
    Guid TenantId,
    string Name,
    AiAgentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// The channels this agent answers on that the reader may see (Phase 37.5a,
    /// row 18). Empty for a reader with neither channel permission.
    /// </summary>
    public IReadOnlyList<AiAgentChannelView> Channels { get; init; } = [];
}
