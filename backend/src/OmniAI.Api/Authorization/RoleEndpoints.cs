using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Authorization;
using OmniAI.Application.Authorization.CreateTenantRole;
using OmniAI.Application.Authorization.GetPermissionCatalog;
using OmniAI.Application.Authorization.GetTenantRoleById;
using OmniAI.Application.Authorization.ListTenantRoles;
using OmniAI.Application.Authorization.RenameTenantRole;
using OmniAI.Application.Authorization.ReplaceTenantRolePermissions;
using OmniAI.Application.Concurrency;
using OmniAI.Domain.Authorization;

namespace OmniAI.Api.Authorization;

/// <summary>
/// The role administration HTTP surface, nested under the workspace that owns it.
/// </summary>
/// <remarks>
/// Minimal API endpoint mapping, matching the style the tenant and AI agent
/// endpoints use. No controller is introduced: mixing two API styles in one
/// application means two places to look for routing, filters and model binding.
/// <para>
/// <strong>Five role operations, and the ones that are absent are the design.</strong>
/// There is no <c>DELETE</c>, because role deletion has a real unanswered question
/// behind it -- what happens to the members holding the role -- and answering it
/// in passing would be worse than leaving it. There is no member-assignment
/// endpoint, no invitation endpoint and no "add member" endpoint, because member
/// administration is a phase of its own; half of it, with a grant and no revoke
/// and no audit, would be a liability rather than a head start.
/// </para>
/// <para>
/// <strong>Reading requires <c>roles.view</c>; writing requires
/// <c>roles.manage</c>.</strong> Neither implies the other -- this platform has no
/// permission implication at all -- so the role editor asks for both, visibly, and
/// a workspace can grant somebody the ability to read its roles without the
/// ability to change them. The checks live inside the use cases rather than here,
/// because an authorization attribute on an endpoint proves only that somebody is
/// signed in.
/// </para>
/// <para>
/// <strong>Creating a role grants the creator nothing.</strong> A role is a
/// definition; holding one requires an assignment, and no endpoint here creates
/// one. Somebody with <c>roles.manage</c> can compose a role granting every
/// capability in the catalogue and still not be able to do any of it.
/// </para>
/// <para>
/// <strong>Both mutations are conditional.</strong> They require <c>If-Match</c>
/// and refuse to act without it, using the same
/// <see cref="EntityTags"/> contract the AI agent endpoints use -- which is why
/// that helper stopped living in the AI agent folder this phase.
/// </para>
/// </remarks>
internal static class RoleEndpoints
{
    /// <summary>Route name for the single-role read endpoint.</summary>
    /// <remarks>
    /// Used to generate the <c>Location</c> header of a successful creation, so
    /// the header and the route cannot drift apart the way a hand-built path
    /// string would.
    /// </remarks>
    private const string GetRoleByIdRouteName = "GetTenantRoleById";

    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder workspace = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}")
            .WithTags("Roles and permissions")
            // Applied to the group, so a route added later inherits both and
            // cannot be published unprotected by omission. Authorization answers
            // 401 for an anonymous caller; membership and permissions are checked
            // below it, in each use case.
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        // The capability catalogue this workspace's roles may be composed from.
        // Under the tenant rather than at the root, because it is published only
        // to somebody entitled to see that workspace's roles -- a rootless route
        // would have nothing to authorise against and would hand the platform's
        // capability list to any signed-in caller.
        workspace.MapGet("/permissions", GetPermissionCatalogAsync)
            .WithName("GetPermissionCatalog")
            .WithSummary("The capability catalogue. Each entry's code is the stable key of its label and description, and groupKey the stable key of its area.")
            .Produces<PermissionResponse[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        workspace.MapGet("/roles", ListAsync)
            .WithName("ListTenantRoles")
            .WithSummary("The workspace's roles, Owner first, each with how many members hold it (memberCount).")
            .Produces<RoleSummaryResponse[]>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        workspace.MapPost("/roles", CreateAsync)
            .WithName("CreateTenantRole")
            .Produces<RoleResponse>(StatusCodes.Status201Created);

        workspace.MapGet("/roles/{roleId:guid}", GetByIdAsync)
            .WithName(GetRoleByIdRouteName)
            .WithSummary("One role with its permissions, an ETag, and how many members hold it (memberCount).")
            .Produces<RoleDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // PUT on named sub-resources, not PATCH on the role. Each replaces one
        // addressable thing whose whole value is in the body, which is what PUT
        // means; a PATCH would need the partial-update semantics this contract
        // deliberately does not define.
        //
        // Two endpoints rather than one, because the two edits fail differently:
        // a name can collide with another role's, and a permission set cannot.
        // Merging them would mean a caller fixing a duplicate name had to resend
        // a permission set they were not changing.
        workspace.MapPut("/roles/{roleId:guid}/name", RenameAsync)
            .WithName("RenameTenantRole")
            .Produces<RoleResponse>(StatusCodes.Status200OK);

        workspace.MapPut("/roles/{roleId:guid}/permissions", ReplacePermissionsAsync)
            .WithName("ReplaceTenantRolePermissions")
            .Produces<RoleResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}/permissions</c>. The capability catalogue.
    /// </summary>
    /// <remarks>
    /// The same list for every workspace, because it describes the product rather
    /// than the tenant. What varies is who may see it. No <c>ETag</c>: the
    /// catalogue is compiled in, so it changes only when the application is
    /// deployed, and publishing a version for it would invite clients to build
    /// conditional requests against something they cannot write to anyway.
    /// </remarks>
    private static async Task<IResult> GetPermissionCatalogAsync(
        Guid tenantId,
        GetPermissionCatalogHandler handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionDefinition>? catalogue = await handler
            .HandleAsync(new GetPermissionCatalogQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return catalogue is null
            ? WorkspaceNotFound(tenantId)
            : Results.Ok(catalogue.Select(PermissionResponse.From).ToArray());
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}/roles</c>. The workspace's roles.
    /// </summary>
    /// <remarks>
    /// No <c>ETag</c>, for the same reason the agent listing has none: a list is
    /// not a resource a caller may write against, so there is no version to
    /// publish. A caller who does not belong to this workspace gets the same
    /// <c>404</c> the tenant endpoints give, raised by the use case.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        Guid tenantId,
        ListTenantRolesHandler handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantRoleSummary> roles = await handler
            .HandleAsync(new ListTenantRolesQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(roles.Select(RoleSummaryResponse.From).ToArray());
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/roles</c>. Returns <c>201 Created</c> with
    /// a <c>Location</c> header and an <c>ETag</c> for the version just stored.
    /// </summary>
    /// <remarks>
    /// No <c>If-Match</c>: there is no previous version of a resource that does
    /// not exist yet, so there is nothing to be out of date about. The
    /// <c>ETag</c> it returns is what lets a client immediately edit the role
    /// without reading it back.
    /// <para>
    /// A rejected name surfaces as <c>400</c>, an unknown permission code as
    /// <c>400</c>, and a name somebody already holds as <c>409</c>. None is caught
    /// here -- see <see cref="ErrorHandling.DomainProblemDetailsHandler"/>.
    /// </para>
    /// </remarks>
    private static async Task<IResult> CreateAsync(
        HttpContext http,
        Guid tenantId,
        CreateRoleRequest request,
        CreateTenantRoleHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Versioned<TenantRoleView> created = await handler
            .HandleAsync(
                new CreateTenantRoleCommand(tenantId, request.Name, request.Permissions),
                cancellationToken)
            .ConfigureAwait(false);

        SetETag(http, created.Version);

        return Results.CreatedAtRoute(
            GetRoleByIdRouteName,
            new { tenantId = created.Value.TenantId, roleId = created.Value.Id },
            RoleResponse.From(created.Value));
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}/roles/{roleId}</c>. Returns <c>200</c> with
    /// an <c>ETag</c>, or <c>404</c> as Problem Details.
    /// </summary>
    /// <remarks>
    /// Its own response record since Phase 37.5b (<see cref="RoleDetailResponse"/>),
    /// with the member count; the writes keep <see cref="RoleResponse"/>.
    /// </remarks>
    private static async Task<IResult> GetByIdAsync(
        HttpContext http,
        Guid tenantId,
        Guid roleId,
        GetTenantRoleByIdHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<TenantRoleDetailView>? role = await handler
            .HandleAsync(new GetTenantRoleByIdQuery(tenantId, roleId), cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return RoleNotFound(roleId);
        }

        SetETag(http, role.Version);

        return Results.Ok(RoleDetailResponse.From(role.Value));
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/roles/{roleId}/name</c>. Requires
    /// <c>If-Match</c>.
    /// </summary>
    /// <remarks>
    /// Answers <c>409</c> for the protected Owner role, and <c>412</c> when the
    /// supplied version is stale -- including for Owner, where staleness is
    /// reported first. See <c>TenantRoleVersionMismatchException</c> for why that
    /// ordering is the right one.
    /// </remarks>
    private static async Task<IResult> RenameAsync(
        HttpContext http,
        Guid tenantId,
        Guid roleId,
        RenameRoleRequest request,
        RenameTenantRoleHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<TenantRoleView>? renamed = await handler
            .HandleAsync(
                new RenameTenantRoleCommand(tenantId, roleId, request.Name, expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, renamed, roleId);
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/roles/{roleId}/permissions</c>. Requires
    /// <c>If-Match</c>.
    /// </summary>
    /// <remarks>
    /// Replaces the whole set in one commit. There is deliberately no grant or
    /// revoke endpoint for an individual permission: a person editing a group of
    /// checkboxes would otherwise be issuing several conditional writes for one
    /// decision, any of which could fail halfway and leave the role granting
    /// something nobody chose.
    /// </remarks>
    private static async Task<IResult> ReplacePermissionsAsync(
        HttpContext http,
        Guid tenantId,
        Guid roleId,
        ReplaceRolePermissionsRequest request,
        ReplaceTenantRolePermissionsHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<TenantRoleView>? updated = await handler
            .HandleAsync(
                new ReplaceTenantRolePermissionsCommand(
                    tenantId,
                    roleId,
                    request.Permissions,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, updated, roleId);
    }

    /// <summary>
    /// The response shared by the read and both mutations: the role with its
    /// <c>ETag</c>, or not-found.
    /// </summary>
    /// <remarks>
    /// One contract for every mutation, chosen over <c>204 No Content</c>. Each of
    /// these changes <c>updatedAt</c> and the version, so a client that needs the
    /// result of its own request would otherwise have to issue a second
    /// <c>GET</c> -- and would meanwhile be showing state it inferred rather than
    /// state the server confirmed. It also carries the new <c>ETag</c>, so
    /// consecutive edits need no re-read.
    /// </remarks>
    private static IResult Respond(HttpContext http, Versioned<TenantRoleView>? role, Guid roleId)
    {
        if (role is null)
        {
            return RoleNotFound(roleId);
        }

        SetETag(http, role.Version);

        return Results.Ok(RoleResponse.From(role.Value));
    }

    /// <summary>Publishes the resource version as a strong entity tag.</summary>
    private static void SetETag(HttpContext http, ConcurrencyToken version) =>
        http.Response.Headers.ETag = EntityTags.Format(version);

    /// <summary>
    /// The single not-found response for every role operation.
    /// </summary>
    /// <remarks>
    /// Identical whether the role does not exist at all, exists under another
    /// workspace, or the caller does not belong to this workspace -- and it says
    /// nothing about which. The wording reports what this workspace has and makes
    /// no claim about the wider system, so it stays accurate without ever
    /// confirming that a role id is real somewhere else.
    /// </remarks>
    private static IResult RoleNotFound(Guid roleId) =>
        Results.Problem(
            title: "Role not found",
            detail: $"This workspace has no role with id '{roleId}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// The not-found response for the catalogue, whose caller is not a member.
    /// </summary>
    /// <remarks>
    /// Worded as a missing tenant rather than a missing catalogue, because that is
    /// the honest description: the catalogue exists, and this caller has no
    /// workspace to read it for. It matches what the tenant endpoints answer for
    /// the same caller, so the two cannot be told apart.
    /// </remarks>
    private static IResult WorkspaceNotFound(Guid tenantId) =>
        Results.Problem(
            title: "Tenant not found",
            detail: $"No tenant exists with id '{tenantId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
