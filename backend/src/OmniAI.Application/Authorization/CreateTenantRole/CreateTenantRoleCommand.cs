namespace OmniAI.Application.Authorization.CreateTenantRole;

/// <summary>Asks for a custom role to be created in a workspace.</summary>
/// <param name="TenantId">The workspace it belongs to, from the route.</param>
/// <param name="Name">Its name, as the caller typed it. Validated by the Domain.</param>
/// <param name="Permissions">
/// The capabilities it grants, as codes. <see langword="null"/> and an empty list
/// both mean "grants nothing", which is a legitimate role to create.
/// </param>
/// <remarks>
/// <strong>What the caller cannot supply is the design.</strong> There is no
/// role id -- the aggregate mints one; no kind or system flag -- the factory can
/// only produce a custom role; no timestamps -- the clock supplies them; and no
/// version -- a resource that does not exist yet has nothing to be out of date
/// about. Each of those would be a field a client could set, and the first three
/// are exactly how a caller would otherwise try to mint a second Owner.
/// <para>
/// The tenant comes from the route rather than the body. A body-supplied tenant
/// would be a second place to name the workspace, and the two could disagree --
/// which is the shape a cross-tenant write takes.
/// </para>
/// </remarks>
public sealed record CreateTenantRoleCommand(
    Guid TenantId,
    string? Name,
    IReadOnlyList<string?>? Permissions);
