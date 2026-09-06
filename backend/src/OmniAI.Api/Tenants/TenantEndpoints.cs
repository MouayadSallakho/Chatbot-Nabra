using OmniAI.Api.Security;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.CreateTenant;
using OmniAI.Application.Tenants.GetTenantById;

namespace OmniAI.Api.Tenants;

/// <summary>
/// The tenant onboarding HTTP surface.
/// </summary>
/// <remarks>
/// Minimal API endpoint mapping, matching the style Phase 1 established for
/// <c>/health</c>. No controller is introduced: mixing two API styles in one
/// application means two places to look for routing, filters and model binding.
/// <para>
/// Each endpoint does exactly three things -- translate the request, call one
/// handler, translate the outcome. There is no business logic here, and no
/// database call.
/// </para>
/// <para>
/// <strong>Both endpoints require a signed-in caller, and reading one requires
/// membership of it.</strong> A tenant id in the route addresses a resource; it
/// authorises nothing. Somebody who is signed in but does not belong to the
/// tenant they name is answered exactly as they would be for a tenant that was
/// never created -- so this route cannot be used to find out which workspaces
/// exist. The check is made inside the use case rather than here, because an
/// authorization attribute on an endpoint proves only that somebody is signed
/// in.
/// </para>
/// </remarks>
internal static class TenantEndpoints
{
    /// <summary>Route name for the single-tenant read endpoint.</summary>
    /// <remarks>
    /// Used to generate the <c>Location</c> header of a successful creation, so
    /// the header and the route cannot drift apart the way a hand-built path
    /// string would.
    /// </remarks>
    private const string GetTenantByIdRouteName = "GetTenantById";

    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder tenants = endpoints.MapGroup("/api/tenants")
            .WithTags("Tenants")
            // Nothing here is reachable anonymously. Creating a workspace needs
            // somebody to own it, and reading one needs a membership to check.
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        tenants.MapPost("/", CreateTenantAsync)
            .WithName("CreateTenant");

        tenants.MapGet("/{tenantId:guid}", GetTenantByIdAsync)
            .WithName(GetTenantByIdRouteName);

        return endpoints;
    }

    /// <summary>
    /// <c>POST /api/tenants</c>. Returns <c>201 Created</c> with a
    /// <c>Location</c> header pointing at the read endpoint.
    /// </summary>
    /// <remarks>
    /// A rejected name or slug surfaces as <c>400</c>, and a taken slug as
    /// <c>409</c>. Neither is caught here: both are translated centrally by
    /// <see cref="ErrorHandling.TenantProblemDetailsHandler"/>, so the mapping
    /// exists once rather than once per endpoint.
    /// </remarks>
    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        CreateTenantHandler handler,
        CancellationToken cancellationToken)
    {
        TenantView created = await handler
            .HandleAsync(new CreateTenantCommand(request.Name, request.Slug), cancellationToken)
            .ConfigureAwait(false);

        return Results.CreatedAtRoute(
            GetTenantByIdRouteName,
            new { tenantId = created.Id },
            TenantResponse.From(created));
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}</c>. Returns <c>200</c>, or <c>404</c> as
    /// Problem Details when no such tenant exists.
    /// </summary>
    /// <remarks>
    /// The <c>:guid</c> route constraint means a non-GUID segment never reaches
    /// this method; it simply does not match the route.
    /// </remarks>
    private static async Task<IResult> GetTenantByIdAsync(
        Guid tenantId,
        GetTenantByIdHandler handler,
        CancellationToken cancellationToken)
    {
        TenantView? tenant = await handler
            .HandleAsync(new GetTenantByIdQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            // Results.Problem, not Results.NotFound: the API answers every error
            // in one contract, and an empty 404 body would be the exception.
            return Results.Problem(
                title: "Tenant not found",
                detail: $"No tenant exists with id '{tenantId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(TenantResponse.From(tenant));
    }
}
