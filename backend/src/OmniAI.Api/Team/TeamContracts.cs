using System.Globalization;
using OmniAI.Application.Invitations;
using OmniAI.Application.Invitations.CreateTenantInvitation;
using OmniAI.Application.Members;
using OmniAI.Application.Members.GetAssignableRoles;
using OmniAI.Domain.Invitations;

namespace OmniAI.Api.Team;

/// <summary>One role a member holds.</summary>
public sealed record MemberRoleResponse(Guid Id, string Name, bool IsSystem)
{
    public static MemberRoleResponse From(TenantMemberRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new MemberRoleResponse(role.Id, role.Name, role.IsSystem);
    }
}

/// <summary>A member, in a list.</summary>
public sealed record MemberSummaryResponse(
    Guid UserId,
    string Email,
    DateTimeOffset JoinedAt,
    IReadOnlyList<MemberRoleResponse> Roles,
    bool IsOwner)
{
    public static MemberSummaryResponse From(TenantMemberSummary member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new MemberSummaryResponse(
            member.UserId,
            member.Email,
            member.JoinedAt,
            [.. member.Roles.Select(MemberRoleResponse.From)],
            member.IsOwner);
    }
}

/// <summary>A page of members.</summary>
public sealed record MemberPageResponse(
    IReadOnlyList<MemberSummaryResponse> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>One member, in full.</summary>
/// <remarks>
/// The permission union is published because it is what an administrator needs
/// before changing somebody's access, and it is the same set the authority
/// ceiling is judged on -- so a refusal is comprehensible rather than mysterious.
/// Nothing about how the account authenticates appears here.
/// </remarks>
public sealed record MemberResponse(
    Guid UserId,
    Guid TenantId,
    string Email,
    DateTimeOffset JoinedAt,
    DateTimeOffset AccessUpdatedAt,
    IReadOnlyList<MemberRoleResponse> Roles,
    bool IsOwner,
    IReadOnlyList<string> Permissions)
{
    public static MemberResponse From(TenantMemberView member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new MemberResponse(
            member.UserId,
            member.TenantId,
            member.Email,
            member.JoinedAt,
            member.AccessUpdatedAt,
            [.. member.Roles.Select(MemberRoleResponse.From)],
            member.IsOwner,
            [.. member.Permissions.Select(code => code.Value)]);
    }
}

/// <summary>A role this caller may hand out.</summary>
public sealed record AssignableRoleResponse(Guid Id, string Name, int PermissionCount)
{
    public static AssignableRoleResponse From(AssignableRole role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new AssignableRoleResponse(role.Id, role.Name, role.PermissionCount);
    }
}

/// <summary>The complete intended set of roles for a member.</summary>
/// <remarks>
/// Only role ids. The caller does not supply the member, the workspace, a
/// permission list or a version in the body -- the first two are in the route,
/// the third is derived from the roles, and the version travels in
/// <c>If-Match</c> where a precondition belongs.
/// </remarks>
public sealed record ReplaceMemberRolesRequest(IReadOnlyList<Guid>? RoleIds);

/// <summary>Whom to invite, and what they should be able to do.</summary>
public sealed record CreateInvitationRequest(string? Email, IReadOnlyList<Guid>? RoleIds);

/// <summary>An invitation, as any later read publishes it.</summary>
/// <remarks>
/// There is no field here for the token or its digest, and there is deliberately
/// no shape of this record that could carry one.
/// </remarks>
public sealed record InvitationResponse(
    Guid Id,
    string Email,
    string Status,
    IReadOnlyList<string> Roles,
    string? InvitedByEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public static InvitationResponse From(TenantInvitationView invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        return new InvitationResponse(
            invitation.Id,
            invitation.Email,
            Serialise(invitation.Status),
            invitation.Roles,
            invitation.InvitedByEmail,
            invitation.CreatedAt,
            invitation.ExpiresAt);
    }

    /// <summary>
    /// The status as a stable name.
    /// </summary>
    /// <remarks>
    /// The name, never the underlying number: reordering the enum would silently
    /// change what every stored client believed a status meant.
    /// </remarks>
    public static string Serialise(TenantInvitationStatus status) =>
        status.ToString();
}

/// <summary>A page of invitations.</summary>
public sealed record InvitationPageResponse(
    IReadOnlyList<InvitationResponse> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// A newly created invitation, with its secret.
/// </summary>
/// <param name="Invitation">The invitation itself.</param>
/// <param name="Token">
/// The raw secret, present in this one response and never again. The endpoint
/// that produces it sets <c>Cache-Control: no-store</c>, because a bearer
/// credential must not be written to any intermediary's disk.
/// </param>
/// <remarks>
/// A separate type from <see cref="InvitationResponse"/> rather than the same
/// record with an optional field. If one type could carry the token, every
/// endpoint returning that type would have to be checked for whether it ever
/// populated it -- and a listing that accidentally did would hand every
/// administrator every outstanding invitation.
/// </remarks>
public sealed record CreatedInvitationResponse(InvitationResponse Invitation, string Token)
{
    public static CreatedInvitationResponse From(
        IssuedInvitation issued,
        DateTimeOffset now,
        string? invitedByEmail)
    {
        ArgumentNullException.ThrowIfNull(issued);

        TenantInvitation invitation = issued.Invitation;

        return new CreatedInvitationResponse(
            new InvitationResponse(
                invitation.Id.Value,
                invitation.Email.Value,
                InvitationResponse.Serialise(
                    invitation.HasExpiredAt(now)
                        ? TenantInvitationStatus.Expired
                        : invitation.Status),
                [.. issued.Roles.Select(role => role.Name.Value).Order(StringComparer.OrdinalIgnoreCase)],
                invitedByEmail,
                invitation.CreatedAt,
                invitation.ExpiresAt),
            issued.Token);
    }
}

/// <summary>The token from an invitation link.</summary>
public sealed record AcceptInvitationRequest(string? Token);

/// <summary>The workspace an invitation admitted somebody to.</summary>
public sealed record AcceptedInvitationResponse(Guid TenantId, string Name, string Slug)
{
    public static AcceptedInvitationResponse From(Application.Invitations.AcceptTenantInvitation.AcceptedInvitation accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);

        return new AcceptedInvitationResponse(accepted.TenantId, accepted.TenantName, accepted.TenantSlug);
    }
}

/// <summary>Paging read from the query string.</summary>
internal static class TeamPaging
{
    /// <summary>Reads a positive integer, or falls back.</summary>
    /// <remarks>
    /// A malformed value falls back rather than failing: <c>?page=abc</c> is a
    /// client defect, and answering the first page is more useful than a
    /// validation error about a parameter the person did not type.
    /// </remarks>
    public static int Read(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
        && parsed > 0
            ? parsed
            : fallback;
}
