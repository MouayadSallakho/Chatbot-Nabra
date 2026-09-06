using OmniAI.Domain.Authorization;

namespace OmniAI.Application.Authorization;

/// <summary>
/// What a set of roles adds up to.
/// </summary>
/// <remarks>
/// <strong>One definition of the union, used by everything that needs one.</strong>
/// The caller's own authority, a colleague's authority when deciding whether they
/// may be reassigned, and an inviter's authority when their invitation is finally
/// accepted are the same computation asked about three different people. Writing
/// it three times is how two of them end up subtly disagreeing -- one forgetting
/// that Owner stores no rows, another forgetting to deduplicate.
/// <para>
/// <strong>Why this is not a method on <c>TenantAuthorizer</c>.</strong> That type
/// deliberately has no parameter naming a user: every question it answers is
/// about the caller, which is what stops it from becoming a way to enumerate a
/// colleague's authority. Phase 14 genuinely does need to reason about somebody
/// else -- but as an internal step of a use case that has already established the
/// caller may do so, never as a question a client can ask. Keeping it here rather
/// than widening the authorizer preserves that property exactly.
/// </para>
/// <para>
/// Ordered by the catalogue rather than alphabetically, matching every other
/// permission list this platform publishes, so two members with the same
/// authority produce byte-identical responses.
/// </para>
/// </remarks>
public static class EffectivePermissions
{
    /// <summary>The union of everything these roles permit.</summary>
    /// <remarks>
    /// Reads each role's effective permissions rather than its stored grants,
    /// which is the distinction that matters for exactly one role: an Owner
    /// stores none and permits everything.
    /// </remarks>
    public static IReadOnlyList<PermissionCode> Union(IEnumerable<TenantRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        HashSet<PermissionCode> held = [.. roles.SelectMany(role => role.EffectivePermissions)];

        return [.. PlatformPermissions.AllCodes.Where(held.Contains)];
    }

    /// <summary>
    /// Whether everything in <paramref name="permissions"/> is within
    /// <paramref name="ceiling"/>.
    /// </summary>
    /// <remarks>
    /// The subset test the delegation rules are written in. The empty set is
    /// within every ceiling, which is why a role granting nothing and a member
    /// holding nothing are always permissible.
    /// </remarks>
    public static bool Within(
        IEnumerable<PermissionCode> permissions,
        IReadOnlyCollection<PermissionCode> ceiling)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(ceiling);

        HashSet<PermissionCode> allowed = [.. ceiling];

        return permissions.All(allowed.Contains);
    }

    /// <summary>
    /// The first permission outside the ceiling, in catalogue order, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// Catalogue order rather than encounter order, so a request exceeding the
    /// ceiling in several ways always names the same one first and two identical
    /// requests are refused identically.
    /// </remarks>
    public static PermissionCode? FirstBeyond(
        IEnumerable<PermissionCode> permissions,
        IReadOnlyCollection<PermissionCode> ceiling)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(ceiling);

        HashSet<PermissionCode> allowed = [.. ceiling];
        HashSet<PermissionCode> wanted = [.. permissions];

        foreach (PermissionCode code in PlatformPermissions.AllCodes)
        {
            if (wanted.Contains(code) && !allowed.Contains(code))
            {
                return code;
            }
        }

        return null;
    }
}
