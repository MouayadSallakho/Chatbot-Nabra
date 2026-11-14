using OmniAI.Application.Authorization;
using OmniAI.Domain.Authorization;

namespace OmniAI.Application.Members;

/// <summary>
/// Turning a stored member into what the Team screens are told.
/// </summary>
/// <remarks>
/// One place where a member becomes a response, so the list and the detail agree
/// about what a role looks like, how they are ordered, and which of them is
/// protected. Two projections written separately are how a list ends up calling
/// somebody an Owner that the detail screen does not.
/// </remarks>
internal static class TenantMembers
{
    /// <summary>Whether this set of roles includes the protected Owner role.</summary>
    public static bool IsOwner(IEnumerable<TenantRole> roles) =>
        roles.Any(role => role.IsSystem);

    /// <summary>
    /// A member's roles, Owner first and then by name.
    /// </summary>
    /// <remarks>
    /// The same order the role list publishes, so the protected role is where a
    /// reader has learned to expect it rather than sorted into the middle by an
    /// alphabetical accident.
    /// </remarks>
    public static IReadOnlyList<TenantMemberRole> Roles(IEnumerable<TenantRole> roles) =>
    [
        .. roles
            .OrderBy(role => role.IsSystem ? 0 : 1)
            .ThenBy(role => role.Name.Normalised, StringComparer.Ordinal)
            .ThenBy(role => role.Id.Value)
            .Select(role => new TenantMemberRole(role.Id.Value, role.Name.Value, role.IsSystem))
    ];

    /// <summary>A member, as a row in the Team list.</summary>
    public static TenantMemberSummary Summarise(TenantMemberRecord member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new TenantMemberSummary(
            member.UserId.Value,
            member.Email.Value,
            member.JoinedAt,
            Roles(member.Roles),
            IsOwner(member.Roles),
            member.DisplayName);
    }

    /// <summary>A member, in full.</summary>
    public static TenantMemberView Describe(TenantMemberRecord member, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new TenantMemberView(
            member.UserId.Value,
            tenantId,
            member.Email.Value,
            member.JoinedAt,
            member.AccessUpdatedAt,
            Roles(member.Roles),
            IsOwner(member.Roles),
            EffectivePermissions.Union(member.Roles),
            member.DisplayName);
    }
}
