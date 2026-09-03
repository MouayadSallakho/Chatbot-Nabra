namespace OmniAI.Domain.Tenants;

/// <summary>
/// Operational state of a <see cref="Tenant"/>.
/// </summary>
/// <remarks>
/// Deliberately limited to the two states the core lifecycle needs today.
/// Trial, past-due, cancelled and archived are billing and retention concerns;
/// modelling them here before those features exist would encode guesses about
/// rules that have not been decided. There is no zero member, so an
/// uninitialised value is detectable rather than silently meaning "Active".
/// </remarks>
public enum TenantStatus
{
    /// <summary>The tenant may use the platform.</summary>
    Active = 1,

    /// <summary>Platform use is withheld; the tenant and its data still exist.</summary>
    Suspended = 2,
}
