using OmniAI.Application.AiAgents;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tests.AiAgents;

/// <summary>
/// A hand-written stand-in for the channel reader (Phase 37.5a).
/// </summary>
/// <remarks>
/// It records which kinds each request asked for, so a test can prove a kind the
/// reader may not see was never asked for at all, and it filters by workspace
/// exactly as the real reader does.
/// </remarks>
internal sealed class InMemoryAiAgentChannelReader : IAiAgentChannelReader
{
    private readonly List<(TenantId Tenant, AiAgentId Agent, AiAgentChannelView Channel)> _channels = [];

    /// <summary>A reader that knows no channels, for tests about something else.</summary>
    public static InMemoryAiAgentChannelReader None => new();

    /// <summary>The kinds each call asked for, in order.</summary>
    public List<AiAgentChannelKinds> Requests { get; } = [];

    public InMemoryAiAgentChannelReader With(TenantId tenant, AiAgentId agent, AiAgentChannelView channel)
    {
        _channels.Add((tenant, agent, channel));

        return this;
    }

    public Task<IReadOnlyDictionary<AiAgentId, IReadOnlyList<AiAgentChannelView>>> ListAsync(
        TenantId tenantId,
        IReadOnlyCollection<AiAgentId> agentIds,
        AiAgentChannelKinds kinds,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(kinds);

        IReadOnlyDictionary<AiAgentId, IReadOnlyList<AiAgentChannelView>> found = _channels
            .Where(entry => entry.Tenant == tenantId
                && agentIds.Contains(entry.Agent)
                && kinds.HasFlag(entry.Channel.Kind == AiAgentChannelKind.WebChat
                    ? AiAgentChannelKinds.WebChat
                    : AiAgentChannelKinds.WhatsApp))
            .GroupBy(entry => entry.Agent)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AiAgentChannelView>)[.. group.Select(entry => entry.Channel)]);

        return Task.FromResult(found);
    }
}
