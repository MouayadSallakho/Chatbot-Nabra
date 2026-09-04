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
/// <strong>These endpoints are unauthenticated.</strong> Authentication and
/// authorisation are not implemented yet, so anyone who can reach the process can
/// create a tenant. They are development-stage functionality and are not
/// authorised for public production exposure until the authentication phase is
/// complete. No placeholder API key, bearer token or admin secret is present,
/// because a fake control would only hide that fact.
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
            .WithTags("Tenants");

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
