using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.GetWebChatEndpointById;

/// <summary>One website chat endpoint of one workspace.</summary>
public sealed record GetWebChatEndpointByIdQuery(Guid TenantId, Guid WebChatEndpointId);

/// <summary>Reads one endpoint, with the version a change must present back.</summary>
public sealed class GetWebChatEndpointByIdHandler(
    IWebChatEndpointRepository endpoints,
    IAiAgentRepository agents,
    TenantAuthorizer authorizer)
{
    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly IAiAgentRepository _agents =
        agents ?? throw new ArgumentNullException(nameof(agents));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <returns>
    /// The endpoint and its version, or <see langword="null"/> when the caller is
    /// not entitled or no such endpoint belongs to this workspace. The two are
    /// deliberately indistinguishable.
    /// </returns>
    public async Task<Versioned<WebChatEndpointView>?> HandleAsync(
        GetWebChatEndpointByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);
        WebChatEndpointId endpointId = WebChatEndpointId.From(query.WebChatEndpointId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.WebsiteChatView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        Versioned<WebChatEndpoint>? stored = await _endpoints
            .FindAsync(tenantId, endpointId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return null;
        }

        Versioned<AiAgent>? agent = await _agents
            .FindAsync(tenantId, stored.Value.AiAgentId, cancellationToken)
            .ConfigureAwait(false);

        // The agent is read through the tenant-scoped lookup, so an endpoint can
        // only ever be shown beside an agent of the same workspace. A missing one
        // would mean a broken foreign key rather than a permission question, and
        // the screen says so plainly instead of pretending the endpoint is fine.
        return new Versioned<WebChatEndpointView>(
            WebChatEndpointView.From(
                stored.Value,
                agent?.Value.Name.Value ?? "Unknown agent",
                agent?.Value.Status ?? AiAgentStatus.Disabled),
            stored.Version);
    }
}
