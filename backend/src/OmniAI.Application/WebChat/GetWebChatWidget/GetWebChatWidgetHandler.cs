using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.GetWebChatWidget;

public sealed record GetWebChatWidgetQuery(Guid TenantId, Guid WebChatEndpointId);

/// <summary>Reads a widget's configuration, with the version needed to change it.</summary>
public sealed class GetWebChatWidgetHandler(
    IWebChatEndpointRepository endpoints,
    TenantAuthorizer authorizer)
{
    private readonly IWebChatEndpointRepository _endpoints =
        endpoints ?? throw new ArgumentNullException(nameof(endpoints));

    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    public async Task<Versioned<WebChatWidgetConfigurationView>?> HandleAsync(
        GetWebChatWidgetQuery query,
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

        return stored is null
            ? null
            : new Versioned<WebChatWidgetConfigurationView>(
                WebChatWidgetConfiguration.From(stored.Value),
                stored.Version);
    }
}

/// <summary>Builds the configuration view from an endpoint.</summary>
internal static class WebChatWidgetConfiguration
{
    public static WebChatWidgetConfigurationView From(WebChatEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new WebChatWidgetConfigurationView(
            endpoint.Id.Value,
            endpoint.PublicKey.Value,
            endpoint.Status.ToString(),
            WebChatWidgetView.From(endpoint.Widget),
            [.. endpoint.AllowedOrigins
                .Select(allowed => allowed.Origin.Value)
                .Order(StringComparer.Ordinal)],
            WebChatEndpoint.MaxAllowedOrigins);
    }
}
