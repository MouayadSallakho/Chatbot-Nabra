using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Authorization;

/// <summary>
/// The fact that a member of a workspace holds one of its roles.
/// </summary>
/// <remarks>
/// <strong>This is the second half of the authorisation boundary.</strong> A
/// <see cref="TenantMembership"/> answers "does this person belong to this
/// workspace"; this answers "what are they allowed to do inside it". The two are
/// deliberately separate rows: a member with no assignments belongs to the
/// workspace and can do nothing in it, which is a state the platform must be able
/// to represent -- it is what somebody looks like the moment before anyone
/// decides what they should be able to do.
/// <para>
/// <strong>It carries the tenant, and that is not redundant.</strong> The user and
/// the role would each imply a tenant on their own, so a naive design would store
/// only <c>(user_id, tenant_role_id)</c>. Carrying <c>tenant_id</c> explicitly is
/// what lets the database hold the rule that all three agree: one composite
/// foreign key ties <c>(user_id, tenant_id)</c> to a real membership, another ties
/// <c>(tenant_role_id, tenant_id)</c> to a role in that same tenant, and both
/// reference the same column. A row pairing a membership in one workspace with a
/// role from another cannot be written -- not because the application declines to
/// write it, but because PostgreSQL refuses it.
/// </para>
/// <para>
/// <strong>Nothing in this phase creates one except workspace provisioning.</strong>
/// There is no invitation, no member administration and no endpoint that assigns a
/// role to anybody. The creator of a workspace receives Owner in the same
/// transaction that creates the workspace, and that is the only production path
/// that writes an assignment. Member administration is a phase of its own, and
/// building half of it here -- a grant endpoint with no revoke, no invitation and
/// no audit -- would be worse than not building it.
/// </para>
/// <para>
/// Three columns and a timestamp, with no <c>grantedBy</c> and no <c>expiresAt</c>.
/// Both would be fields nothing fills: there is no assignment workflow to record
/// an actor for, and no product rule that expires authority. They arrive with the
/// phase that needs them, when their meaning has been decided.
/// </para>
/// </remarks>
public sealed class TenantRoleAssignment
{
    private TenantRoleAssignment(
        UserId userId,
        TenantId tenantId,
        TenantRoleId tenantRoleId,
        DateTimeOffset createdAt)
    {
        UserId = userId;
        TenantId = tenantId;
        TenantRoleId = tenantRoleId;
        CreatedAt = createdAt;
    }

    /// <summary>The person who holds the role.</summary>
    public UserId UserId { get; }

    /// <summary>The workspace the membership and the role both belong to.</summary>
    public TenantId TenantId { get; }

    /// <summary>The role they hold.</summary>
    public TenantRoleId TenantRoleId { get; }

    /// <summary>When the role was assigned.</summary>
    /// <remarks>
    /// Supplied by the caller from the injected clock, like every other timestamp
    /// in this model, so rows written in one transaction carry the instant that
    /// transaction chose rather than three readings of a system clock.
    /// </remarks>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Assigns a role to a member.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// An identifier is the default value. All three are validated by their own
    /// factories before they can reach this method; this catches the one path that
    /// bypasses them -- a <c>default</c> struct -- because an empty user id would
    /// match every other empty user id.
    /// </exception>
    public static TenantRoleAssignment Create(
        UserId userId,
        TenantId tenantId,
        TenantRoleId tenantRoleId,
        DateTimeOffset createdAt)
    {
        if (userId.IsEmpty)
        {
            throw new DomainValidationException("A role assignment must name a user.");
        }

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A role assignment must name a tenant.");
        }

        if (tenantRoleId.IsEmpty)
        {
            throw new DomainValidationException("A role assignment must name a role.");
        }

        return new TenantRoleAssignment(userId, tenantId, tenantRoleId, createdAt);
    }

    /// <summary>
    /// Assigns a role to a member, taking the workspace from the role itself.
    /// </summary>
    /// <remarks>
    /// The overload provisioning uses, and the safer of the two: the tenant is
    /// read off the role rather than supplied beside it, so the caller has no
    /// opportunity to pair a role with the wrong workspace. Where the tenant is
    /// passed separately -- rehydrating a stored row, or a test constructing a
    /// deliberate mismatch -- <see cref="Create"/> is available and the database's
    /// composite foreign keys remain the guarantee.
    /// </remarks>
    public static TenantRoleAssignment ForRole(
        UserId userId,
        TenantRole role,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(role);

        return Create(userId, role.TenantId, role.Id, createdAt);
    }
}
