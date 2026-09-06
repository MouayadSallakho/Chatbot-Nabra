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
/// <strong>Three fields, and deliberately no fourth.</strong> There is no role,
/// no permission set, no invitation state and no "is owner" flag. Today every
/// member of a workspace is its creator, so a role column would hold the same
/// value in every row and would be a schema commitment made before anybody had
/// decided what the roles are. Roles, permissions and invitations get their own
/// phase, and that phase must land before member administration is exposed --
/// until then, "member" means full access to the workspace and the code says so
/// plainly rather than implying a gradation that does not exist.
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
    private TenantMembership(UserId userId, TenantId tenantId, DateTimeOffset createdAt)
    {
        UserId = userId;
        TenantId = tenantId;
        CreatedAt = createdAt;
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

        return new TenantMembership(userId, tenantId, createdAt);
    }
}
