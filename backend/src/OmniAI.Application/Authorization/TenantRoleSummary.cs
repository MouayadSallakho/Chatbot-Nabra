using OmniAI.Domain.Authorization;

namespace OmniAI.Application.Authorization;

/// <summary>
/// One row of the workspace's role list.
/// </summary>
/// <param name="Id">The role's identity.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Kind">Whether it is the protected system role or a custom one.</param>
/// <param name="PermissionCount">How many capabilities it grants.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When its name or permissions last changed.</param>
/// <remarks>
/// <strong>A count, not the set.</strong> The list exists so somebody can see
/// which roles a workspace has and pick one; the permissions themselves are on
/// the detail screen, one request away. Sending every role's full set with the
/// list would grow with the catalogue for a screen that renders a number, and
/// would tempt a client to build an editor on data it fetched for a table.
/// <para>
/// <strong>For an Owner the count is the catalogue's size, not a row count.</strong>
/// Owner stores no grants, so counting rows would report zero -- a number that is
/// true of the table and false about the role. The count comes from the same place
/// the authority does.
/// </para>
/// <para>
/// No tenant id: every caller of this list already named the workspace in the
/// route, so repeating it in each row would be noise. The detail view carries it,
/// where a client holding one role out of context might need it.
/// </para>
/// </remarks>
public sealed record TenantRoleSummary(
    Guid Id,
    string Name,
    TenantRoleKind Kind,
    int PermissionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
