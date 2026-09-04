namespace OmniAI.Application.Tenants.CreateTenant;

/// <summary>
/// Request to onboard a new tenant.
/// </summary>
/// <param name="Name">Display name, exactly as the caller supplied it.</param>
/// <param name="Slug">Slug, exactly as the caller supplied it.</param>
/// <remarks>
/// The strings are raw and unvalidated on purpose. Validating them here would
/// duplicate rules that <c>TenantName</c> and <c>TenantSlug</c> already own, and
/// two copies of a rule drift. The handler converts them into value objects and
/// the Domain decides whether they are acceptable.
/// <para>
/// There is no id and no status field, so a caller cannot choose either. A
/// client-chosen tenant id would let one caller collide with -- or guess -- another
/// tenant's identity, and a client-chosen status would let a suspended tenant
/// onboard itself as active.
/// </para>
/// </remarks>
public sealed record CreateTenantCommand(string? Name, string? Slug);
