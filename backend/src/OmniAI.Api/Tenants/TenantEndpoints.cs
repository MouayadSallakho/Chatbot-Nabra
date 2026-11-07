using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Tenants;
using OmniAI.Application.Tenants.ChangeTimeZone;
using OmniAI.Application.Tenants.CreateTenant;
using OmniAI.Application.Tenants.GetTenantById;
using OmniAI.Application.Tenants.RenameTenant;

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
            .WithName(GetTenantByIdRouteName)
            .WithSummary("The workspace, with the ETag a rename must send in If-Match.")
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Phase 37.5a (D-3): the owner controls the name visitors see.
        tenants.MapPut("/{tenantId:guid}/name", RenameTenantAsync)
            .WithName("RenameTenant")
            .WithSummary("Renames the workspace. Requires workspace.manage and If-Match; visitors see the new name at once.")
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // Phase 37.6 (row 34): the zone analytics, reports and schedules count in.
        tenants.MapPut("/{tenantId:guid}/time-zone", ChangeTimeZoneAsync)
            .WithName("ChangeTenantTimeZone")
            .WithSummary("Sets the workspace time zone (an IANA name). Requires workspace.manage and If-Match.")
            .Produces<TenantResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);

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
        HttpContext http,
        Guid tenantId,
        GetTenantByIdHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<TenantView>? tenant = await handler
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

        // Phase 37.5a: the version a rename must name. The body is unchanged.
        http.Response.Headers.ETag = EntityTags.Format(tenant.Version);

        return Results.Ok(TenantResponse.From(tenant.Value));
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/name</c>. Requires <c>If-Match</c> and
    /// <c>workspace.manage</c>. Returns <c>200</c> with the workspace and its new
    /// <c>ETag</c> (Phase 37.5a, row 20a).
    /// </summary>
    /// <remarks>
    /// <c>428</c> without <c>If-Match</c>, <c>412</c> when it is stale,
    /// <c>400 workspace.name.invalid</c> for a name the creation rules refuse,
    /// <c>403</c> for a member without the permission and <c>404</c> for anybody
    /// else. Each real rename writes one <c>workspace.renamed</c> security event.
    /// The hosted and embedded chat reads show the new name on their next read.
    /// </remarks>
    private static async Task<IResult> RenameTenantAsync(
        HttpContext http,
        Guid tenantId,
        RenameTenantRequest? request,
        RenameTenantHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<TenantView>? renamed = await handler
            .HandleAsync(new RenameTenantCommand(tenantId, request?.Name, expected), cancellationToken)
            .ConfigureAwait(false);

        if (renamed is null)
        {
            return Results.Problem(
                title: "Tenant not found",
                detail: $"No tenant exists with id '{tenantId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        http.Response.Headers.ETag = EntityTags.Format(renamed.Version);

        return Results.Ok(TenantResponse.From(renamed.Value));
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/time-zone</c>. Requires <c>If-Match</c> and
    /// <c>workspace.manage</c>. Returns <c>200</c> with the workspace and its new
    /// <c>ETag</c> (Phase 37.6, row 34).
    /// </summary>
    /// <remarks>
    /// <c>428</c> without <c>If-Match</c>, <c>412</c> when it is stale,
    /// <c>400 workspace.time_zone.invalid</c> for a name that is not an IANA zone
    /// both this runtime and PostgreSQL know, <c>403</c> for a member without the
    /// permission and <c>404</c> for anybody else. Report schedules still to come
    /// move to the new zone's next boundary in the same transaction.
    /// </remarks>
    private static async Task<IResult> ChangeTimeZoneAsync(
        HttpContext http,
        Guid tenantId,
        ChangeTenantTimeZoneRequest? request,
        ChangeTenantTimeZoneHandler handler,
        CancellationToken cancellationToken)
    {
        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<TenantView>? changed = await handler
            .HandleAsync(new ChangeTenantTimeZoneCommand(tenantId, request?.TimeZone, expected), cancellationToken)
            .ConfigureAwait(false);

        if (changed is null)
        {
            return Results.Problem(
                title: "Tenant not found",
                detail: $"No tenant exists with id '{tenantId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        http.Response.Headers.ETag = EntityTags.Format(changed.Version);

        return Results.Ok(TenantResponse.From(changed.Value));
    }
}
