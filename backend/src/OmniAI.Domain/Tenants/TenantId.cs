using System.Globalization;

namespace OmniAI.Domain.Tenants;

/// <summary>
/// Identity of a <see cref="Tenant"/>.
/// </summary>
/// <remarks>
/// A dedicated type rather than a bare <see cref="Guid"/>, so that a tenant
/// identity cannot be silently interchanged with any other identifier as the
/// model grows. Tenant identity is the isolation boundary of the whole platform,
/// which makes accidental substitution a security concern, not a style concern.
/// <para>
/// New values use UUID version 7. Its leading timestamp gives generated keys
/// natural ordering, which keeps index insertions local instead of scattering
/// them across a B-tree the way version 4 does.
/// </para>
/// </remarks>
public readonly record struct TenantId
{
    private TenantId(Guid value) => Value = value;

    /// <summary>The underlying identifier.</summary>
    public Guid Value { get; }

    /// <summary>
    /// True for the uninitialised <c>default</c> value. Every construction path
    /// rejects an empty identifier, but C# always permits <c>default(TenantId)</c>
    /// for a struct, so consumers that accept an identity from outside the Domain
    /// should reject an empty one explicitly.
    /// </summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Generates a new identity.</summary>
    public static TenantId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing identifier.</summary>
    /// <exception cref="DomainValidationException">The identifier is empty.</exception>
    public static TenantId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Tenant id must not be an empty GUID.");
        }

        return new TenantId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
