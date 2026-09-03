namespace OmniAI.Domain.Tenants;

/// <summary>
/// A business account using the platform, and the root of the isolation boundary
/// that every tenant-owned record will hang from.
/// </summary>
/// <remarks>
/// State changes go through intentional operations rather than public setters,
/// so an invariant cannot be bypassed by assigning a property. The aggregate is
/// kept deliberately narrow: billing, plans, quotas, branding, channel and AI
/// configuration are separate concerns that will own their own types rather than
/// accumulate here.
/// </remarks>
public sealed class Tenant
{
    private Tenant(TenantId id, TenantName name, TenantSlug slug, TenantStatus status)
    {
        Id = id;
        Name = name;
        Slug = slug;
        Status = status;
    }

    /// <summary>Identity. Fixed for the lifetime of the tenant.</summary>
    public TenantId Id { get; }

    /// <summary>Display name.</summary>
    public TenantName Name { get; private set; }

    /// <summary>Human-readable identifier.</summary>
    public TenantSlug Slug { get; private set; }

    /// <summary>Current operational state.</summary>
    public TenantStatus Status { get; private set; }

    /// <summary>
    /// Registers a new tenant. A fresh identity is generated and the tenant
    /// starts <see cref="TenantStatus.Active"/>: onboarding gates belong to the
    /// use case that admits a tenant, not to the shape of the tenant itself.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="slug"/> is null.</exception>
    public static Tenant Create(TenantName name, TenantSlug slug)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(slug);

        return new Tenant(TenantId.New(), name, slug, TenantStatus.Active);
    }

    /// <summary>Changes the display name.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public void Rename(TenantName name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
    }

    /// <summary>
    /// Changes the human-readable identifier. Whether the new slug is already
    /// taken by another tenant cannot be decided here; that check belongs to the
    /// use case and to the unique index behind it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="slug"/> is null.</exception>
    public void ChangeSlug(TenantSlug slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        Slug = slug;
    }

    /// <summary>
    /// Withholds platform use. Idempotent: suspending an already suspended
    /// tenant is a no-op rather than an error, because the caller's intent --
    /// "this tenant must not be active" -- is already satisfied, and forcing
    /// callers to check the current state first would invite races without
    /// preventing anything.
    /// </summary>
    public void Suspend() => Status = TenantStatus.Suspended;

    /// <summary>
    /// Restores platform use. Idempotent, for the same reason as
    /// <see cref="Suspend"/>.
    /// </summary>
    public void Activate() => Status = TenantStatus.Active;
}
