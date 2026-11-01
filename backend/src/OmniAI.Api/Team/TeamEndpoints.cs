using OmniAI.Api.Http;
using OmniAI.Api.Auth;
using OmniAI.Api.Security;
using OmniAI.Application.Accounts;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Invitations.AcceptTenantInvitation;
using OmniAI.Application.Invitations.CreateTenantInvitation;
using OmniAI.Application.Invitations.ListTenantInvitations;
using OmniAI.Application.Invitations.RevokeTenantInvitation;
using OmniAI.Application.Invitations;
using OmniAI.Application.Members.GetAssignableRoles;
using OmniAI.Application.Members.GetTenantMemberById;
using OmniAI.Application.Members.ListTenantMembers;
using OmniAI.Application.Members.ReplaceTenantMemberRoles;
using OmniAI.Application.Members;
using OmniAI.Application.Security;
using OmniAI.Domain.Users;

namespace OmniAI.Api.Team;

/// <summary>
/// Team administration: who is in a workspace, and who is being invited.
/// </summary>
/// <remarks>
/// <strong>Three questions before any of these does its work.</strong> Is
/// somebody signed in -- <c>401</c> from the pipeline. Do they belong to the
/// workspace in the route -- <c>404</c>, the same answer as for a workspace that
/// never existed. Do their roles carry the permission this operation needs --
/// <c>403</c>. The last two are decided inside each use case, below HTTP, where
/// no route mapping can forget them.
/// <para>
/// <strong>Acceptance is the one route outside the workspace group.</strong> It
/// cannot be under <c>/api/tenants/{tenantId}</c>, because the person accepting
/// does not yet belong to that workspace and does not necessarily know which one
/// the link is for. The token identifies the invitation and the session
/// identifies the person; nothing else is accepted from the caller.
/// </para>
/// <para>
/// <strong>There is no member-removal route, and no route that assigns
/// Owner.</strong> Removing somebody needs last-owner and self-removal semantics
/// that have not been designed, and ownership transfer is a deliberate act with
/// its own consequences. Neither is approximated here.
/// </para>
/// </remarks>
internal static class TeamEndpoints
{
    private const string GetMemberByIdRouteName = "GetTenantMemberById";

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder workspace = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}")
            .WithTags("Team")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        workspace.MapGet("/members", ListMembersAsync)
            .WithName("ListTenantMembers");

        // Before the {userId} route, so "assignable-roles" is never mistaken for
        // an identifier. It would not match the guid constraint, but relying on a
        // constraint to disambiguate two routes is a coincidence rather than a
        // decision.
        workspace.MapGet("/members/assignable-roles", GetAssignableRolesAsync)
            .WithName("GetAssignableRoles");

        workspace.MapGet("/members/{userId:guid}", GetMemberAsync)
            .WithName(GetMemberByIdRouteName);

        workspace.MapPut("/members/{userId:guid}/roles", ReplaceMemberRolesAsync)
            .WithName("ReplaceTenantMemberRoles");

        workspace.MapGet("/invitations", ListInvitationsAsync)
            .WithName("ListTenantInvitations");

        workspace.MapPost("/invitations", CreateInvitationAsync)
            .WithName("CreateTenantInvitation");

        // POST to a named action rather than DELETE on the invitation, because
        // the invitation is not deleted: it keeps its history, and the record that
        // somebody withdrew it is part of what a workspace can look back at.
        workspace.MapPost("/invitations/{invitationId:guid}/revoke", RevokeInvitationAsync)
            .WithName("RevokeTenantInvitation");

        // Outside the workspace group, and authenticated rather than membership
        // checked -- the whole point is that the caller is not a member yet.
        endpoints
            .MapPost("/api/invitations/accept", AcceptInvitationAsync)
            .WithTags("Team")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .WithName("AcceptTenantInvitation");

        return endpoints;
    }

    /// <summary><c>GET /api/tenants/{tenantId}/members</c>.</summary>
    private static async Task<IResult> ListMembersAsync(
        Guid tenantId,
        HttpContext http,
        ListTenantMembersHandler handler,
        CancellationToken cancellationToken)
    {
        int page = TeamPaging.Read(http.Request.Query["page"], 1);
        int pageSize = TeamPaging.Read(
            http.Request.Query["pageSize"],
            ListTenantMembersHandler.DefaultPageSize);

        TenantMemberPage? members = await handler
            .HandleAsync(new ListTenantMembersQuery(tenantId, page, pageSize), cancellationToken)
            .ConfigureAwait(false);

        return members is null
            ? WorkspaceNotFound(tenantId)
            : Results.Ok(new MemberPageResponse(
                [.. members.Items.Select(MemberSummaryResponse.From)],
                members.Page,
                members.PageSize,
                members.Total));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/members/{userId}</c>.</summary>
    /// <remarks>
    /// Carries the <c>ETag</c> a role reassignment must present back. Published to
    /// anybody who may read the member, because whether they may also write is a
    /// different question the write itself answers.
    /// </remarks>
    private static async Task<IResult> GetMemberAsync(
        Guid tenantId,
        Guid userId,
        HttpContext http,
        GetTenantMemberByIdHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<TenantMemberView>? member = await handler
            .HandleAsync(new GetTenantMemberByIdQuery(tenantId, userId), cancellationToken)
            .ConfigureAwait(false);

        if (member is null)
        {
            return MemberNotFound(userId);
        }

        SetETag(http, member.Version);

        return Results.Ok(MemberResponse.From(member.Value));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/members/assignable-roles</c>.</summary>
    private static async Task<IResult> GetAssignableRolesAsync(
        Guid tenantId,
        GetAssignableRolesHandler handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AssignableRole>? roles = await handler
            .HandleAsync(new GetAssignableRolesQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return roles is null
            ? WorkspaceNotFound(tenantId)
            : Results.Ok(roles.Select(AssignableRoleResponse.From).ToArray());
    }

    /// <summary><c>PUT /api/tenants/{tenantId}/members/{userId}/roles</c>.</summary>
    /// <remarks>
    /// Conditional, like every other client-edited resource on this platform: the
    /// caller presents the version they read, and a save composed against a
    /// picture somebody else has already replaced is refused rather than applied.
    /// </remarks>
    private static async Task<IResult> ReplaceMemberRolesAsync(
        Guid tenantId,
        Guid userId,
        ReplaceMemberRolesRequest request,
        HttpContext http,
        ReplaceTenantMemberRolesHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<TenantMemberView>? member = await handler
            .HandleAsync(
                new ReplaceTenantMemberRolesCommand(tenantId, userId, request.RoleIds, expected),
                cancellationToken)
            .ConfigureAwait(false);

        if (member is null)
        {
            return MemberNotFound(userId);
        }

        SetETag(http, member.Version);

        return Results.Ok(MemberResponse.From(member.Value));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/invitations</c>.</summary>
    private static async Task<IResult> ListInvitationsAsync(
        Guid tenantId,
        HttpContext http,
        ListTenantInvitationsHandler handler,
        CancellationToken cancellationToken)
    {
        int page = TeamPaging.Read(http.Request.Query["page"], 1);
        int pageSize = TeamPaging.Read(
            http.Request.Query["pageSize"],
            ListTenantInvitationsHandler.DefaultPageSize);

        TenantInvitationPage? invitations = await handler
            .HandleAsync(
                new ListTenantInvitationsQuery(tenantId, page, pageSize),
                cancellationToken)
            .ConfigureAwait(false);

        return invitations is null
            ? WorkspaceNotFound(tenantId)
            : Results.Ok(new InvitationPageResponse(
                [.. invitations.Items.Select(InvitationResponse.From)],
                invitations.Page,
                invitations.PageSize,
                invitations.Total));
    }

    /// <summary>
    /// <c>POST /api/tenants/{tenantId}/invitations</c>. The one response that
    /// carries the secret.
    /// </summary>
    /// <remarks>
    /// <strong><c>Cache-Control: no-store</c>, and it is not optional.</strong>
    /// This body contains a bearer credential. Without the header a proxy, a
    /// browser's back-forward cache or a debugging tool would be entitled to keep
    /// a copy on disk, and an invitation link would outlive the moment it was
    /// shown. <c>no-store</c> is the only directive that forbids writing it down
    /// at all -- <c>no-cache</c> merely requires revalidation, which is not the
    /// same promise.
    /// </remarks>
    private static async Task<IResult> CreateInvitationAsync(
        Guid tenantId,
        CreateInvitationRequest request,
        HttpContext http,
        CreateTenantInvitationHandler handler,
        IAccountDirectory accounts,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Phase 37.3 (row 8a): the invitation email's language, optional.
        if (!AccountProblems.TryReadLanguage(request.Language, out UserLanguage? language, out IResult? unsupported))
        {
            return unsupported!;
        }

        IssuedInvitation issued = await handler
            .HandleAsync(
                new CreateTenantInvitationCommand(tenantId, request.Email, request.RoleIds, language),
                cancellationToken)
            .ConfigureAwait(false);

        http.Response.Headers.CacheControl = "no-store";

        Domain.Users.EmailAddress? inviter = await accounts
            .FindEmailAsync(issued.Invitation.InvitedByUserId, cancellationToken)
            .ConfigureAwait(false);

        return Results.Created(
            $"/api/tenants/{tenantId}/invitations/{issued.Invitation.Id.Value}",
            CreatedInvitationResponse.From(issued, clock.GetUtcNow(), inviter?.Value));
    }

    /// <summary><c>POST /api/tenants/{tenantId}/invitations/{invitationId}/revoke</c>.</summary>
    private static async Task<IResult> RevokeInvitationAsync(
        Guid tenantId,
        Guid invitationId,
        RevokeTenantInvitationHandler handler,
        CancellationToken cancellationToken)
    {
        bool revoked = await handler
            .HandleAsync(new RevokeTenantInvitationCommand(tenantId, invitationId), cancellationToken)
            .ConfigureAwait(false);

        return revoked
            ? Results.NoContent()
            : Results.Problem(
                title: "Invitation not found",
                detail: $"No invitation exists with id '{invitationId}'.",
                statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// <c>POST /api/invitations/accept</c>.
    /// </summary>
    /// <remarks>
    /// <strong>Authenticated, but deliberately not membership-checked.</strong>
    /// Every other tenant-scoped route in this platform requires the caller to
    /// already belong to the workspace; this is the one that creates that
    /// membership, so it cannot. What replaces the membership check is the pair of
    /// facts the use case insists on: an unguessable token, and a signed-in
    /// account whose address is the one invited.
    /// <para>
    /// <c>no-store</c> here too. The response carries no secret, but the request
    /// did, and a cached entry keyed on it would be a copy of the token.
    /// </para>
    /// </remarks>
    private static async Task<IResult> AcceptInvitationAsync(
        AcceptInvitationRequest request,
        HttpContext http,
        AcceptTenantInvitationHandler handler,
        ICurrentUser currentUser,
        IAccountLanguages languages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(languages);

        http.Response.Headers.CacheControl = "no-store";

        if (!AccountProblems.TryReadLanguage(request.Language, out UserLanguage? requested, out IResult? unsupported))
        {
            return unsupported!;
        }

        AcceptedInvitation? accepted = await handler
            .HandleAsync(new AcceptTenantInvitationCommand(request.Token), cancellationToken)
            .ConfigureAwait(false);

        // Phase 37.3: the language the person is using fills their account's, only
        // while it has none. A chosen one is never overwritten here.
        if (accepted is not null
            && currentUser.Id is Domain.Users.UserId me
            && AccountProblems.Preferred(http, requested) is UserLanguage language)
        {
            await languages.FillIfUnsetAsync(me, language, cancellationToken).ConfigureAwait(false);
        }

        // A token that matches nothing is answered exactly as a token that was
        // never issued, and nothing distinguishes a mistyped value from a probe.
        return accepted is null
            ? Results.Problem(
                title: "Invitation not found",
                detail: "This invitation link is not valid.",
                statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(AcceptedInvitationResponse.From(accepted));
    }

    private static void SetETag(HttpContext http, ConcurrencyToken version) =>
        http.Response.Headers.ETag = EntityTags.Format(version);

    /// <summary>The answer a non-member gets, identical to a missing workspace.</summary>
    private static IResult WorkspaceNotFound(Guid tenantId) =>
        Results.Problem(
            title: "Tenant not found",
            detail: $"No tenant exists with id '{tenantId}'.",
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// The answer for a member this workspace does not have.
    /// </summary>
    /// <remarks>
    /// The same answer whether the account exists elsewhere or not at all, so this
    /// route cannot be used to discover who has registered on the platform.
    /// </remarks>
    private static IResult MemberNotFound(Guid userId) =>
        Results.Problem(
            title: "Member not found",
            detail: $"This workspace has no member with id '{userId}'.",
            statusCode: StatusCodes.Status404NotFound);
}
