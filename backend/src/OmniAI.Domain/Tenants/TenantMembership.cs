using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tenants;

/// <summary>
/// The fact that a person belongs to a tenant.
/// </summary>
/// <remarks>
/// <strong>This is the authorization boundary of the platform.</strong> A tenant
/// id in a route says only <em>which</em> resource is being addressed; it grants
/// nothing. Access requires that a membership row exists pairing the
/// authenticated user with that tenant. Knowing a tenant id is not, and must
/// never become, a way to reach its data.
/// <para>
/// <strong>Still no role column, and that is the point.</strong> There is no
/// permission set, no invitation state and no "is owner" flag. Which roles a
/// person holds is a set, not a value, so it lives in its own table -- a column
/// here would have forced one role per member before anybody knew whether that
/// was the right shape, and it is not.
/// </para>
/// <para>
/// <strong>Why it gained <see cref="UpdatedAt"/> in Phase 14.</strong> Role
/// assignment became something two administrators can edit at the same time, so
/// it needs the platform's accepted optimistic-concurrency contract: read a
/// version, present it back, be refused if somebody else got there first. The
/// assignments live in a join table, and a change there does not by itself move
/// the parent row's version -- so without a field of its own, a membership's
/// <c>ETag</c> would stay the same while the thing it describes changed, and the
/// precondition would never refuse anything. This is a real business fact
/// (&quot;when did this person's access last change&quot;) rather than a marker
/// invented to make a row look dirty, which is why it is a timestamp and not a
/// counter nobody would ever read.
/// </para>
/// <para>
/// <strong>Identity-free.</strong> It holds a <see cref="Users.UserId"/>, which is
/// a value, not an account. No password hash, no email, no security stamp and no
/// ASP.NET Core Identity type appears here or anywhere else in the Domain --
/// credentials belong to the identity provider in Infrastructure, and an
/// executable guard asserts the Domain references nothing outside the base class
/// library.
/// </para>
/// <para>
/// The pair <c>(UserId, TenantId)</c> is the identity of this entity, and is the
/// primary key in storage. That is not a persistence convenience: a person either
/// belongs to a tenant or does not, so a second row for the same pair has no
/// meaning, and a surrogate key would allow one to exist.
/// </para>
/// </remarks>
public sealed class TenantMembership
{
    private TenantMembership(
        UserId userId,
        TenantId tenantId,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        UserId = userId;
        TenantId = tenantId;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>The person who belongs to the tenant.</summary>
    public UserId UserId { get; }

    /// <summary>The tenant they belong to.</summary>
    public TenantId TenantId { get; }

    /// <summary>When the membership was granted.</summary>
    /// <remarks>
    /// Supplied by the caller from the injected clock rather than read here, for
    /// the same reason every other timestamp in this model is: a type that reads
    /// the system clock cannot be tested deterministically, and two rows written
    /// in one transaction should carry the instant that transaction chose.
    /// </remarks>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When this person's access last changed.</summary>
    /// <remarks>
    /// Equal to <see cref="CreatedAt"/> for a membership nobody has re-assigned,
    /// including every membership that existed before Phase 14 -- which is what
    /// the migration backfills it to, so no row has to invent a moment that never
    /// happened.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Records that this person's roles were reassigned.
    /// </summary>
    /// <remarks>
    /// <strong>It never goes backwards.</strong> Two administrators saving within
    /// the same clock tick, or a clock that steps back, must not make a membership
    /// look older than it is -- so an earlier instant is ignored rather than
    /// written. The precondition that actually decides who wins a race is the
    /// stored row version, not this timestamp; this is what a person reads.
    /// </remarks>
    public void MarkRolesChanged(DateTimeOffset now)
    {
        if (now > UpdatedAt)
        {
            UpdatedAt = now;
        }
    }

    /// <summary>Grants a membership.</summary>
    /// <exception cref="DomainValidationException">
    /// Either identifier is the default value. Both are validated by their own
    /// factories before they can reach this method; this catches the one path
    /// that bypasses them -- a <c>default</c> struct -- because a membership with
    /// an empty user id would match every other empty user id.
    /// </exception>
    public static TenantMembership Create(UserId userId, TenantId tenantId, DateTimeOffset createdAt)
    {
        if (userId.IsEmpty)
        {
            throw new DomainValidationException(
                "A tenant membership must name a user.");
        }

        if (tenantId.IsEmpty)
        {
            throw new DomainValidationException(
                "A tenant membership must name a tenant.");
        }

        return new TenantMembership(userId, tenantId, createdAt, createdAt);
    }
}
