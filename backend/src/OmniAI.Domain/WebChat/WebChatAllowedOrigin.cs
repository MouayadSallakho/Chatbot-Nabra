using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.WebChat;

/// <summary>
/// One website a workspace has approved its widget to be embedded on.
/// </summary>
/// <remarks>
/// <para>
/// A row rather than an entry in a JSON blob on the endpoint. That is what lets
/// the database refuse a duplicate outright, and it is what makes "which
/// workspaces allow this origin" a question that can be asked at all. A list
/// serialised into a column is a list nothing can constrain.
/// </para>
/// <para>
/// <strong>The workspace is carried on the row rather than inferred.</strong> It is
/// what the composite foreign key uses, so an origin can only ever belong to an
/// endpoint in the same workspace — the same device that keeps knowledge documents
/// and agents from crossing a tenant boundary.
/// </para>
/// </remarks>
public sealed class WebChatAllowedOrigin
{
    private WebChatAllowedOrigin(
        WebChatEndpointId endpointId,
        TenantId tenantId,
        WebChatOrigin origin,
        DateTimeOffset allowedAt)
    {
        EndpointId = endpointId;
        TenantId = tenantId;
        Origin = origin;
        AllowedAt = allowedAt;
    }

    public WebChatEndpointId EndpointId { get; }

    public TenantId TenantId { get; }

    public WebChatOrigin Origin { get; }

    /// <summary>When somebody approved this website.</summary>
    public DateTimeOffset AllowedAt { get; }

    internal static WebChatAllowedOrigin For(
        WebChatEndpointId endpointId,
        TenantId tenantId,
        WebChatOrigin origin,
        DateTimeOffset allowedAt)
    {
        ArgumentNullException.ThrowIfNull(origin);

        return new WebChatAllowedOrigin(endpointId, tenantId, origin, allowedAt.ToUniversalTime());
    }
}
