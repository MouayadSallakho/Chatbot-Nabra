using System.Globalization;

namespace OmniAI.Domain.Authorization;

/// <summary>
/// Identity of a <see cref="TenantRole"/>.
/// </summary>
/// <remarks>
/// Follows the accepted <c>TenantId</c>, <c>AiAgentId</c> and <c>UserId</c>
/// design: a distinct type so a role identity cannot be silently interchanged
/// with any other identifier, and UUID version 7 for new values so generated keys
/// stay time-ordered and insert locally in an index.
/// <para>
/// The substitution this type prevents is not hypothetical here. A role id
/// appears in a route beside a tenant id, and both are GUIDs; a bare
/// <see cref="Guid"/> parameter would accept either, and a transposition would
/// compile. What makes that dangerous rather than merely wrong is that the pair
/// is what scopes a role to its tenant, so a swap is the shape a cross-tenant
/// modification would take.
/// </para>
/// </remarks>
public readonly record struct TenantRoleId
{
    private TenantRoleId(Guid value) => Value = value;

    /// <summary>The underlying identifier.</summary>
    public Guid Value { get; }

    /// <summary>
    /// True for the uninitialised <c>default</c> value. No factory can produce an
    /// empty identifier, but C# always permits <c>default(TenantRoleId)</c> for a
    /// struct, so boundaries accepting an identity from outside the Domain should
    /// reject an empty one explicitly.
    /// </summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Generates a new identity.</summary>
    public static TenantRoleId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing identifier.</summary>
    /// <exception cref="DomainValidationException">The identifier is empty.</exception>
    public static TenantRoleId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Tenant role id must not be an empty GUID.");
        }

        return new TenantRoleId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
