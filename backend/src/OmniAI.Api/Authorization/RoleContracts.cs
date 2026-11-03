using OmniAI.Application.Authorization;
using OmniAI.Domain.Authorization;

namespace OmniAI.Api.Authorization;

/// <summary>What a caller may send when creating a custom role.</summary>
/// <param name="Name">The role's name.</param>
/// <param name="Permissions">
/// The capability codes it grants. Omitted or <see langword="null"/> means it
/// grants nothing, which is a legitimate role to create.
/// </param>
/// <remarks>
/// <strong>Two fields, and the omissions are the security design.</strong> There
/// is no id, no tenant id, no kind or system flag, no timestamps and no version.
/// A caller cannot ask for an <c>Owner</c>, cannot choose a role's identity and
/// cannot claim a role belongs to a workspace other than the one in the route --
/// not because those are validated away, but because there is nowhere to put
/// them. The tenant comes from the route, and the aggregate decides the rest.
/// <para>
/// Nullable, like every other request contract in this API: model binding fills
/// what arrived, and the Domain decides whether it is acceptable. A non-nullable
/// property would make a missing field a framework error message rather than this
/// platform's own.
/// </para>
/// </remarks>
public sealed record CreateRoleRequest(string? Name, IReadOnlyList<string?>? Permissions);

/// <summary>What a caller may send when renaming a role.</summary>
public sealed record RenameRoleRequest(string? Name);

/// <summary>What a caller may send when replacing a role's permissions.</summary>
/// <param name="Permissions">
/// The complete intended set, not a delta. Omitting a code removes it; omitting
/// the field entirely leaves the role granting nothing.
/// </param>
public sealed record ReplaceRolePermissionsRequest(IReadOnlyList<string?>? Permissions);

/// <summary>One row of the workspace's role list.</summary>
/// <param name="Id">The role's identity.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Type">
/// <c>System</c> or <c>Custom</c>. Published as a string rather than the enum's
/// spelling, so adding a kind without deciding its public name fails loudly.
/// </param>
/// <param name="IsSystem">
/// Whether it is protected from renaming and permission changes. Derived from
/// <paramref name="Type"/> and sent beside it so a client does not have to know
/// which types are protected in order to decide whether to render an editor.
/// </param>
/// <param name="PermissionCount">How many capabilities it grants.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When its name or permissions last changed.</param>
/// <param name="MemberCount">
/// How many members hold it (Phase 37.5b), so an editor can say "saving changes
/// access for 3 members" before anybody presses save.
/// </param>
public sealed record RoleSummaryResponse(
    Guid Id,
    string Name,
    string Type,
    bool IsSystem,
    int PermissionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MemberCount)
{
    /// <summary>Projects a summary.</summary>
    public static RoleSummaryResponse From(TenantRoleSummary role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new RoleSummaryResponse(
            role.Id,
            role.Name,
            RoleTypes.Serialise(role.Kind),
            role.Kind is TenantRoleKind.Owner,
            role.PermissionCount,
            role.CreatedAt,
            role.UpdatedAt,
            role.MemberCount);
    }
}

/// <summary>One role, with the capabilities it grants.</summary>
/// <param name="Id">The role's identity.</param>
/// <param name="TenantId">The workspace it belongs to.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Type"><c>System</c> or <c>Custom</c>.</param>
/// <param name="IsSystem">Whether it is protected.</param>
/// <param name="Permissions">
/// What holding this role permits, as codes, in the catalogue's order. For the
/// Owner role this is the complete current catalogue rather than a stored set.
/// </param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
/// <remarks>
/// No version field. Which stored revision this is travels in the <c>ETag</c>
/// header, so the body stays a description of the role and a client cannot
/// mistake a persistence marker for part of it.
/// </remarks>
public sealed record RoleResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Type,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a role.</summary>
    public static RoleResponse From(TenantRoleView role)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new RoleResponse(
            role.Id,
            role.TenantId,
            role.Name,
            RoleTypes.Serialise(role.Kind),
            role.IsSystem,
            [.. role.Permissions.Select(code => code.Value)],
            role.CreatedAt,
            role.UpdatedAt);
    }
}

/// <summary>
/// One role as <c>GET …/roles/{id}</c> shows it: <see cref="RoleResponse"/>'s
/// fields and how many members hold it (Phase 37.5b, row 23).
/// </summary>
/// <remarks>
/// The read's own record, so the writes keep their exact shape and never claim a
/// count they did not read.
/// </remarks>
public sealed record RoleDetailResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Type,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MemberCount)
{
    /// <summary>Projects a role's detail.</summary>
    public static RoleDetailResponse From(TenantRoleDetailView detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        RoleResponse role = RoleResponse.From(detail.Role);

        return new RoleDetailResponse(
            role.Id,
            role.TenantId,
            role.Name,
            role.Type,
            role.IsSystem,
            role.Permissions,
            role.CreatedAt,
            role.UpdatedAt,
            detail.MemberCount);
    }
}

/// <summary>One capability a role may be given.</summary>
/// <param name="Code">
/// The security contract. This is the only field authorisation ever compares, and
/// the only one a client should send back.
/// </param>
/// <param name="Group">The product area, for grouping the checkboxes.</param>
/// <param name="Label">The short name a person sees.</param>
/// <param name="Description">One sentence saying what it actually allows.</param>
/// <param name="GroupKey">
/// The stable key of <paramref name="Group"/>, such as <c>ai-agents</c> (Phase
/// 37.5b). A client translates the area by this key, and the label and description
/// by <paramref name="Code"/>; the English texts may change, the keys never do.
/// </param>
/// <remarks>
/// No internal type name, no handler name, no route and no ordinal. A client can
/// render this and send codes back, and learns nothing about how the platform is
/// built from it.
/// </remarks>
public sealed record PermissionResponse(
    string Code,
    string Group,
    string Label,
    string Description,
    string GroupKey)
{
    /// <summary>Projects a catalogue entry.</summary>
    public static PermissionResponse From(PermissionDefinition permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        return new PermissionResponse(
            permission.Code.Value,
            permission.Group,
            permission.Label,
            permission.Description,
            permission.GroupKey);
    }
}

/// <summary>The wire names for a role's kind.</summary>
/// <remarks>
/// Written out rather than left to <c>ToString()</c> or a JSON converter, for the
/// same reason <c>TenantResponse</c> does it for tenant status: the mapping is the
/// published contract, so adding a kind without deciding its public name should
/// fail loudly rather than leak an enum member's spelling.
/// </remarks>
internal static class RoleTypes
{
    public static string Serialise(TenantRoleKind kind) => kind switch
    {
        TenantRoleKind.Owner => "System",
        TenantRoleKind.Custom => "Custom",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "This role kind has no published API representation."),
    };
}
