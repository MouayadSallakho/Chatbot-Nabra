namespace OmniAI.Domain.Authorization;

/// <summary>
/// Whether a role is one the platform defines or one the workspace wrote.
/// </summary>
/// <remarks>
/// Two members, and there is deliberately no third. A "system" role that a tenant
/// could edit would not be a system role, and a "custom" role the platform
/// maintained would not be custom -- the distinction is exactly whether the
/// workspace owns the contents, and anything between the two would have to
/// explain who wins when they disagree.
/// <para>
/// Stored by name rather than by ordinal, like every other enum in this schema,
/// so the meaning of a stored row does not depend on the declaration order here.
/// </para>
/// </remarks>
public enum TenantRoleKind
{
    /// <summary>
    /// The protected <c>Owner</c> role, created with the workspace.
    /// </summary>
    /// <remarks>
    /// Exactly one exists per tenant, enforced by a partial unique index. It
    /// cannot be renamed, cannot have its permissions replaced, and cannot be
    /// created by any caller -- workspace provisioning is the only thing that
    /// makes one. Its authority is the whole permission catalogue, read at the
    /// moment a question is asked rather than stored as rows.
    /// </remarks>
    Owner = 0,

    /// <summary>
    /// A role the workspace created and maintains.
    /// </summary>
    /// <remarks>
    /// Its name and its permission set are both editable, and its permissions are
    /// exactly the grants stored against it -- which may legitimately be none.
    /// </remarks>
    Custom = 1,
}
