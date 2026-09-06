using OmniAI.Api.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.ListMyTenants;
using OmniAI.Domain.Tenants;

namespace OmniAI.Api.Auth;

/// <summary>
/// What the signed-in person has, as opposed to who they are.
/// </summary>
/// <remarks>
/// A separate group from <c>/api/auth</c> on purpose. <c>/api/auth</c> is about
/// establishing and ending a session; <c>/api/me</c> is about the platform
/// resources that session reaches. Putting the workspace list under the
/// authentication group would suggest membership is part of the credential, which
/// is exactly the misconception this phase exists to prevent -- membership is a
/// row in the database, checked on every request, not something baked into a
/// cookie at sign-in.
/// </remarks>
internal static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder me = endpoints.MapGroup("/api/me")
            .WithTags("Current user")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        me.MapGet("/tenants", ListWorkspacesAsync)
            .WithName("ListMyTenants");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /api/me/tenants</c>. The workspaces this person belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is where the admin application's list of usable tenant ids
    /// comes from, and the only place.</strong> Before this phase the browser
    /// chose a tenant id and the backend believed it; now the browser may only
    /// choose from what this endpoint returned, and every tenant-scoped request is
    /// checked against membership again regardless. Editing the stored selection
    /// in the browser therefore achieves nothing: the next request for that tenant
    /// answers <c>404</c>.
    /// </para>
    /// <para>
    /// <strong>It publishes nobody else's membership.</strong> The caller is read
    /// from the authenticated principal and cannot be supplied as a parameter, so
    /// there is no shape of request that asks about another person. Each entry
    /// carries the four fields the tenant itself owns -- no member list, no member
    /// count, no role, no owner.
    /// </para>
    /// <para>
    /// An empty array is the correct answer for somebody who has just registered,
    /// not a <c>404</c>: the collection exists and is empty.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ListWorkspacesAsync(
        ListMyTenantsHandler handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantView> workspaces =
            await handler.HandleAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(workspaces.Select(ToResponse).ToArray());
    }

    private static WorkspaceResponse ToResponse(TenantView tenant) =>
        new(tenant.Id, tenant.Name, tenant.Slug, Serialise(tenant.Status));

    /// <summary>
    /// The wire name for a status.
    /// </summary>
    /// <remarks>
    /// Written out rather than left to <c>ToString()</c> or a JSON converter, for
    /// the same reason <c>TenantResponse</c> does it: the mapping is the published
    /// contract, so adding a status without deciding its public name should fail
    /// loudly rather than leak an enum member's spelling.
    /// </remarks>
    private static string Serialise(TenantStatus status) => status switch
    {
        TenantStatus.Active => "Active",
        TenantStatus.Suspended => "Suspended",
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "This tenant status has no published API representation."),
    };
}
