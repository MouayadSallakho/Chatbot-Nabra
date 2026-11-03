using Microsoft.EntityFrameworkCore;
using OmniAI.Application.AiAgents;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>Which channels a page of agents answers on, over EF Core and PostgreSQL.</summary>
/// <remarks>
/// <para>
/// One statement per visible kind, each naming every agent on the page at once:
/// <c>web_chat_endpoints</c> through <c>ux_web_chat_endpoints_tenant_agent</c> and
/// <c>whatsapp_connections</c> through <c>ix_whatsapp_connections_agent</c>. A kind
/// the reader may not see is never queried, so a reader without either channel
/// permission costs nothing here.
/// </para>
/// <para>
/// Only the id and the status leave the database: no public key, number,
/// allowlist or credential is selected, so none can reach an agent read by a
/// later change to a response shape.
/// </para>
/// </remarks>
internal sealed class AiAgentChannelReader(OmniAiDbContext context) : IAiAgentChannelReader
{
    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyDictionary<AiAgentId, IReadOnlyList<AiAgentChannelView>>> ListAsync(
        TenantId tenantId,
        IReadOnlyCollection<AiAgentId> agentIds,
        AiAgentChannelKinds kinds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentIds);

        Dictionary<AiAgentId, List<AiAgentChannelView>> found = [];

        if (agentIds.Count == 0 || kinds == AiAgentChannelKinds.None)
        {
            return new Dictionary<AiAgentId, IReadOnlyList<AiAgentChannelView>>();
        }

        List<AiAgentId> named = [.. agentIds.Distinct()];

        if (kinds.HasFlag(AiAgentChannelKinds.WebChat))
        {
            var webChat = await _context.WebChatEndpoints
                .AsNoTracking()
                .Where(endpoint => endpoint.TenantId == tenantId && named.Contains(endpoint.AiAgentId))
                .OrderBy(endpoint => endpoint.CreatedAt)
                .ThenBy(endpoint => endpoint.Id)
                .Select(endpoint => new { endpoint.AiAgentId, endpoint.Id, endpoint.Status })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in webChat)
            {
                Add(found, row.AiAgentId, new AiAgentChannelView(
                    AiAgentChannelKind.WebChat,
                    row.Id.Value,
                    row.Status.ToString()));
            }
        }

        if (kinds.HasFlag(AiAgentChannelKinds.WhatsApp))
        {
            var whatsApp = await _context.WhatsAppConnections
                .AsNoTracking()
                .Where(connection => connection.TenantId == tenantId && named.Contains(connection.AiAgentId))
                .OrderBy(connection => connection.CreatedAt)
                .ThenBy(connection => connection.Id)
                .Select(connection => new { connection.AiAgentId, connection.Id, connection.Status })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var row in whatsApp)
            {
                Add(found, row.AiAgentId, new AiAgentChannelView(
                    AiAgentChannelKind.WhatsApp,
                    row.Id.Value,
                    row.Status.ToString()));
            }
        }

        return found.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<AiAgentChannelView>)entry.Value);
    }

    private static void Add(
        Dictionary<AiAgentId, List<AiAgentChannelView>> found,
        AiAgentId agentId,
        AiAgentChannelView channel)
    {
        if (!found.TryGetValue(agentId, out List<AiAgentChannelView>? channels))
        {
            channels = [];
            found[agentId] = channels;
        }

        channels.Add(channel);
    }
}
