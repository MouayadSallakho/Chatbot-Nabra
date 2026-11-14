using OmniAI.Api.Security;
using OmniAI.Application.Accounts;
using OmniAI.Application.Security;
using OmniAI.Domain.Users;
using OmniAI.Application.Authorization.GetMyTenantPermissions;
using OmniAI.Application.Tenants.ListMyTenants;
using OmniAI.Application.Tenants;
using OmniAI.Domain.Authorization;
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
            .WithName("ListMyTenants")
            .Produces<WorkspaceResponse[]>(StatusCodes.Status200OK);

        me.MapGet("/tenants/{tenantId:guid}/permissions", ListMyPermissionsAsync)
            .WithName("ListMyTenantPermissions")
            .Produces<string[]>(StatusCodes.Status200OK);

        // Phase 37.3.
        me.MapPut("/language", ChangeLanguageAsync)
            .WithName("ChangeMyLanguage")
            .WithSummary("Stores the language the server writes to this account in (\"en\" or \"ar\"). The interface keeps its own choice in the browser.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Phase 39.1 (note 7).
        me.MapPut("/display-name", ChangeDisplayNameAsync)
            .WithName("ChangeMyDisplayName")
            .WithSummary("Stores the name colleagues see beside this account's email, or clears it with null or blank. Trimmed, inner whitespace collapsed, at most 80 characters (400 account.display_name.too_long with limit and limitUnit), no control or format characters except ZWJ and ZWNJ (400 account.display_name.control_characters). The caller's own account only; staff-facing, never shown to visitors.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

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

    /// <summary>
    /// <c>GET /api/me/tenants/{tenantId}/permissions</c>. What this person may do
    /// in one of their workspaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is a user-experience input, not a security boundary.</strong>
    /// The admin application asks it so it can stop offering actions that would be
    /// refused. A browser can be told anything and the answer can be edited in a
    /// debugger; every request is authorised again on the server regardless, so a
    /// client that lied to itself here would see buttons it could press and
    /// <c>403</c>s when it did.
    /// </para>
    /// <para>
    /// <strong>Membership is enough; no permission is required.</strong>
    /// Requiring one would be circular -- a member with nothing granted could not
    /// be told they have nothing granted -- and everything in the answer is a fact
    /// about the caller, so there is nothing here to withhold from them.
    /// </para>
    /// <para>
    /// <strong>It reports on nobody else.</strong> There is no user parameter and
    /// no shape of request that names one, so this cannot become a way to
    /// enumerate a colleague's authority. Which members hold which roles belongs to
    /// the member-administration phase, and this endpoint deliberately does not
    /// preview it.
    /// </para>
    /// <para>
    /// An empty array and a <c>404</c> mean genuinely different things: a member
    /// who has been given no roles yet, and somebody this workspace does not know.
    /// The second is the same answer every other tenant-scoped route gives a
    /// non-member.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ListMyPermissionsAsync(
        Guid tenantId,
        GetMyTenantPermissionsHandler handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PermissionCode>? permissions = await handler
            .HandleAsync(new GetMyTenantPermissionsQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return permissions is null
            ? Results.Problem(
                title: "Tenant not found",
                detail: $"No tenant exists with id '{tenantId}'.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(permissions.Select(code => code.Value).ToArray());
    }

    /// <summary>
    /// <c>PUT /api/me/language</c>. Changes the stored language, chosen or not.
    /// </summary>
    /// <remarks>
    /// The only way a chosen language changes (Phase 37.3): sign-in and invitation
    /// acceptance fill it only while it is empty. The account menu calls this when a
    /// signed-in person switches. A missing value is refused like an unknown one.
    /// </remarks>
    private static async Task<IResult> ChangeLanguageAsync(
        Auth.ChangeLanguageRequest request,
        ICurrentUser currentUser,
        IAccountLanguages languages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!UserLanguages.TryParse(request.Language, out UserLanguage language))
        {
            return AccountProblems.Problem(
                StatusCodes.Status400BadRequest,
                "Language not supported",
                "The language must be \"en\" or \"ar\".",
                AccountProblems.LanguageUnsupported);
        }

        UserId me = currentUser.Id ?? throw new NotAuthenticatedException();

        return await languages.SetAsync(me, language, cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.Problem(
                title: "Not authenticated",
                detail: "This request does not identify a signed-in user.",
                statusCode: StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// <c>PUT /api/me/display-name</c>. Changes, or clears, the caller's own name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Only one's own.</strong> The account is the session's; there is no
    /// user id in the route or the body, so there is no request that edits a
    /// colleague's name. No workspace and no permission are involved, as with the
    /// language.
    /// </para>
    /// <para>
    /// No <c>If-Match</c>: last write wins on one's own name. The rules are the
    /// value object's (<see cref="MemberDisplayName"/>), and a refusal reaches the
    /// client with its code through the problem writer.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ChangeDisplayNameAsync(
        Auth.ChangeDisplayNameRequest request,
        ICurrentUser currentUser,
        IAccountDisplayNames displayNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        MemberDisplayName? displayName = MemberDisplayName.CreateOrNone(request.DisplayName);

        UserId me = currentUser.Id ?? throw new NotAuthenticatedException();

        return await displayNames.SetAsync(me, displayName, cancellationToken).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.Problem(
                title: "Not authenticated",
                detail: "This request does not identify a signed-in user.",
                statusCode: StatusCodes.Status401Unauthorized);
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
