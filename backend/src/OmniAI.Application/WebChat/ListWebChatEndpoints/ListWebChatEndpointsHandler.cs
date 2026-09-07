using OmniAI.Application.Authorization;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.WebChat.ListWebChatEndpoints;

/// <summary>The workspace's website chat endpoints.</summary>
/// <param name="TenantId">The workspace to read.</param>
public sealed record ListWebChatEndpointsQuery(Guid TenantId);

/// <summary>Lists how a workspace's website chat is configured.</summary>
public sealed class ListWebChatEndpointsHandler(
    IWebChatEndpointRepository endpoints,
    TenantAuthorizer authorizer)
{
    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    /// <returns>
    /// The endpoints, or <see langword="null"/> when the caller does not belong to
    /// this workspace or lacks the permission to read its channels.
    /// </returns>
    public async Task<IReadOnlyList<WebChatEndpointView>?> HandleAsync(
        ListWebChatEndpointsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        TenantId tenantId = TenantId.From(query.TenantId);

        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.WebsiteChatView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        IReadOnlyList<WebChatEndpointRow> rows = await _endpoints
            .ListAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(WebChatEndpointView.From)];
    }
}
