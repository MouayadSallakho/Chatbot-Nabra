using OmniAI.Domain.Tenants;

namespace OmniAI.Domain.Authorization;

/// <summary>
/// A named set of permissions inside one workspace.
/// </summary>
/// <remarks>
/// <strong>Roles are tenant-scoped, and that is the whole reason this type
/// exists rather than an ASP.NET Core Identity role.</strong> An Identity role is
/// global to the installation: one person is either in it or not, everywhere. In
/// this product the same person may legitimately be the owner of one workspace
/// and a restricted support agent in another, so the role has to belong to the
/// tenant. <see cref="TenantId"/> is set at construction and never changes -- a
/// role cannot move between workspaces, and the database refuses the shape that
/// would let it, through the composite key an assignment must satisfy.
/// <para>
/// <strong>Owner is a system role, and its authority is not stored.</strong> An
/// Owner role holds no permission grants at all: asked what it permits, it
/// answers with the current platform catalogue. That is deliberate, and it is the
/// answer to the failure mode where a capability added in a future release
/// silently leaves every existing Owner without it. There is no row to backfill,
/// no migration to remember and no tenant that can be missed -- adding an entry
/// to <see cref="PlatformPermissions.All"/> extends every Owner in every
/// workspace in the same edit, because the list <em>is</em> what Owner means.
/// </para>
/// <para>
/// <strong>Owner is also protected against the API.</strong> It cannot be
/// renamed, its permissions cannot be replaced, and no caller can create one:
/// <see cref="CreateOwner"/> is reachable only from workspace provisioning, and
/// <see cref="CreateCustom"/> cannot produce one whatever it is passed. Deletion
/// is not implemented anywhere in this phase, so Owner cannot be removed either
/// -- not because a rule forbids it, but because no code path deletes a role.
/// </para>
/// <para>
/// <strong>Only positive grants.</strong> There is no deny, no priority, no
/// parent role and no inheritance. A member's authority is the union of the roles
/// they hold, and that is the entire composition rule -- which means the
/// permissions a role confers can be read off the role, rather than computed
/// against everything else the member has.
/// </para>
/// </remarks>
public sealed class TenantRole
{
    /// <summary>The one name the protected system role ever has.</summary>
    /// <remarks>
    /// A constant rather than a value somebody passes in, so there is no call
    /// site that could name it anything else and no migration that could disagree
    /// with the application about what to look for.
    /// </remarks>
    public const string OwnerRoleName = "Owner";

    private readonly List<RolePermissionGrant> _grants = [];

    private TenantRole(
        TenantRoleId id,
        TenantId tenantId,
        TenantRoleName name,
        TenantRoleKind kind,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Kind = kind;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Identity. Fixed for the lifetime of the role.</summary>
    public TenantRoleId Id { get; }

    /// <summary>
    /// The workspace this role belongs to. Immutable.
    /// </summary>
    /// <remarks>
    /// No setter, no operation that changes it, and no factory that takes an
    /// existing role and a new tenant. Moving a role between workspaces would move
    /// whatever authority it carries with it, into a tenant whose administrators
    /// never agreed to it.
    /// </remarks>
    public TenantId TenantId { get; }

    /// <summary>The name the workspace shows for this role.</summary>
    public TenantRoleName Name { get; private set; }

    /// <summary>Whether this is the protected system role or one the workspace wrote.</summary>
    public TenantRoleKind Kind { get; }

    /// <summary>Whether this role is protected from renaming and permission changes.</summary>
    public bool IsSystem => Kind is TenantRoleKind.Owner;

    /// <summary>When the role was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the role's name or permissions last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// What holding this role actually permits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single question the authorisation path asks of a role, and the single
    /// list the API publishes for one. For Owner it is the whole catalogue; for a
    /// custom role it is the grants that were stored against it.
    /// </para>
    /// <para>
    /// <strong>Unrecognised stored codes are dropped.</strong> A grant naming a
    /// capability this release no longer has -- one retired between versions --
    /// confers nothing and is not published. Filtering here rather than at the
    /// edge means the fail-safe direction is structural: an unknown code can only
    /// ever grant less, never more, and no screen is shown a capability the
    /// product cannot deliver.
    /// </para>
    /// <para>
    /// Ordered, so two roles granting the same set produce the same list. An
    /// unordered set would make responses and test assertions differ for reasons
    /// that carry no meaning.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PermissionCode> EffectivePermissions =>
        IsSystem
            ? PlatformPermissions.AllCodes
            :
            [
                .. _grants
                    .Select(grant => grant.Permission)
                    .Where(PlatformPermissions.IsRecognised)
                    .Distinct()
                    .Order()
            ];

    /// <summary>
    /// The grants stored against this role.
    /// </summary>
    /// <remarks>
    /// Exposed for persistence and for tests that need to see what was written
    /// rather than what it means. Callers deciding authorisation must use
    /// <see cref="EffectivePermissions"/>: for an Owner this collection is empty,
    /// which is emphatically not what the role permits.
    /// </remarks>
    public IReadOnlyCollection<RolePermissionGrant> Grants => _grants;

    /// <summary>
    /// Creates the protected Owner role for a new workspace.
    /// </summary>
    /// <remarks>
    /// Called from workspace provisioning and from the Phase 13 backfill, and
    /// from nowhere else. It takes no name and no permissions, because it has no
    /// choice about either: the name is fixed and the authority is the catalogue.
    /// </remarks>
    /// <exception cref="DomainValidationException">The tenant id is empty.</exception>
    public static TenantRole CreateOwner(TenantId tenantId, DateTimeOffset now)
    {
        RequireTenant(tenantId);

        return new TenantRole(
            TenantRoleId.New(),
            tenantId,
            TenantRoleName.Create(OwnerRoleName),
            TenantRoleKind.Owner,
            now,
            now);
    }

    /// <summary>
    /// Creates a role the workspace defines.
    /// </summary>
    /// <param name="tenantId">The workspace it belongs to.</param>
    /// <param name="name">Its name. Uniqueness within the tenant is the database's to enforce.</param>
    /// <param name="permissions">
    /// The capabilities it grants. May be empty -- a role that grants nothing is a
    /// legitimate thing to create on the way to deciding what it should do, and
    /// refusing it would only invite somebody to grant a placeholder permission
    /// they then forget to remove.
    /// </param>
    /// <param name="now">The creation instant, from the caller's clock.</param>
    /// <remarks>
    /// <strong>It cannot produce a system role.</strong> There is no kind
    /// parameter, so no request body, no test and no future caller can ask for
    /// one. That is what makes "the API must not permit callers to create another
    /// Owner" true by construction rather than by a check somebody could move.
    /// </remarks>
    /// <exception cref="DomainValidationException">
    /// The tenant id is empty, or a permission code is not one this platform
    /// recognises.
    /// </exception>
    public static TenantRole CreateCustom(
        TenantId tenantId,
        TenantRoleName name,
        IEnumerable<PermissionCode> permissions,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(permissions);
        RequireTenant(tenantId);

        TenantRole role = new(
            TenantRoleId.New(),
            tenantId,
            name,
            TenantRoleKind.Custom,
            now,
            now);

        role.SetPermissions(permissions);

        return role;
    }

    /// <summary>
    /// Changes the role's name.
    /// </summary>
    /// <remarks>
    /// Idempotent in effect but not in bookkeeping: renaming to the same name
    /// still stamps <see cref="UpdatedAt"/>, because the caller asked for a write
    /// and the version they hold should move. Suppressing that would make a
    /// successful request look like it had not happened.
    /// </remarks>
    /// <exception cref="DomainStateException">
    /// This is the protected Owner role. Raised as a state conflict rather than a
    /// validation failure because nothing about the request is malformed -- the
    /// same request against a custom role would succeed.
    /// </exception>
    public void Rename(TenantRoleName name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        RequireEditable("renamed");

        Name = name;
        UpdatedAt = now;
    }

    /// <summary>
    /// Replaces the role's entire permission set.
    /// </summary>
    /// <remarks>
    /// <strong>Replace, never merge.</strong> The editor shows one set of
    /// checkboxes and saves it whole, so what arrives is the complete intended
    /// state. Merging would make removing a permission impossible through this
    /// operation and would turn two administrators editing at once into a
    /// silent union of their intentions -- which is the accumulation of authority
    /// nobody asked for.
    /// </remarks>
    /// <exception cref="DomainStateException">This is the protected Owner role.</exception>
    /// <exception cref="DomainValidationException">
    /// A permission code is not one this platform recognises. The whole set is
    /// rejected; nothing is applied.
    /// </exception>
    public void ReplacePermissions(IEnumerable<PermissionCode> permissions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        RequireEditable("given a different permission set");

        SetPermissions(permissions);

        UpdatedAt = now;
    }

    /// <summary>
    /// Validates the whole set, then applies it.
    /// </summary>
    /// <remarks>
    /// Materialised and checked before a single grant is removed, so a set
    /// containing one unrecognised code leaves the role exactly as it was. A
    /// loop that validated and applied in the same pass would empty the role and
    /// then throw, which is the partial replacement this operation must never
    /// produce.
    /// <para>
    /// Duplicates are deduplicated rather than rejected. The set is what the
    /// caller wants the role to grant, and asking for the same capability twice
    /// expresses the same wish as asking once -- there is no interpretation under
    /// which it means something else, so refusing it would be pedantry that a
    /// client has to write code to avoid. The stored result is the same either
    /// way, and the database's composite key guarantees it.
    /// </para>
    /// </remarks>
    private void SetPermissions(IEnumerable<PermissionCode> permissions)
    {
        List<PermissionCode> requested = [.. permissions.Distinct().Order()];

        PlatformPermissions.RequireRecognised(requested);

        _grants.Clear();

        foreach (PermissionCode code in requested)
        {
            _grants.Add(RolePermissionGrant.For(Id, code));
        }
    }

    private void RequireEditable(string attemptedChange)
    {
        if (IsSystem)
        {
            throw new DomainStateException(
                $"The '{Name.Value}' role is a protected system role and cannot be " +
                $"{attemptedChange}. It always grants every permission this platform " +
                "defines. Create a custom role instead.");
        }
    }

    private static void RequireTenant(TenantId tenantId)
    {
        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException("A role must belong to a tenant.");
        }
    }
}
