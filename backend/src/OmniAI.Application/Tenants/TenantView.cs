using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Tenants;

/// <summary>
/// The tenant information a use case hands back to its caller.
/// </summary>
/// <param name="Id">The tenant's identity.</param>
/// <param name="Name">The canonical display name, as the Domain normalised it.</param>
/// <param name="Slug">The canonical slug, as the Domain normalised it.</param>
/// <param name="Status">The tenant's operational state.</param>
/// <param name="TimeZone">The IANA zone its days are counted in (Phase 37.6).</param>
/// <remarks>
/// A read model, not the aggregate. Handing <c>Tenant</c> itself to a caller
/// would expose its behaviour -- <c>Rename</c>, <c>Suspend</c>, <c>Activate</c> --
/// to layers that have no business invoking it, and would make the HTTP contract
/// move whenever the aggregate does.
/// <para>
/// It carries only the five fields the Domain owns today (the zone since Phase 37.6). <c>Status</c> stays the
/// Domain enum: this type is transport-neutral, and choosing how a status appears
/// on the wire is the API's decision, not the Application's. There is deliberately
/// no created/updated timestamp, no plan, no billing and no owner, because the
/// Domain has none of those and inventing them here would create a contract with
/// nothing behind it.
/// </para>
/// </remarks>
public sealed record TenantView(Guid Id, string Name, string Slug, TenantStatus Status, string TimeZone)
{
    /// <summary>Projects an aggregate into its read model.</summary>
    internal static TenantView From(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return new TenantView(tenant.Id.Value, tenant.Name.Value, tenant.Slug.Value, tenant.Status, tenant.TimeZone.Name);
    }

    /// <summary>
    /// How many report schedules changing the zone now would move (Phase 38.7b,
    /// BA-9), or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Set only by the workspace read, and only for a caller who holds
    /// <c>workspace.manage</c>, the one permission that may change the zone.
    /// Everyone else, and every other read model, carries <see langword="null"/>:
    /// absent, never zero, so nobody without the permission learns whether the
    /// workspace has schedules.
    /// </remarks>
    public int? ScheduleCount { get; init; }
}
