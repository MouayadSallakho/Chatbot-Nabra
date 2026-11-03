using System.Globalization;

namespace OmniAI.Domain.Invitations;

/// <summary>
/// The identity of a tenant invitation.
/// </summary>
/// <remarks>
/// A UUIDv7 like every other identifier in this model, so rows written in
/// sequence land in sequence in the index rather than scattering across it.
/// <para>
/// <strong>This is not the invitation's secret.</strong> It appears in listings
/// and in the revoke route, and anybody who may read the workspace's invitations
/// may see it. What lets somebody accept an invitation is a separate, opaque,
/// cryptographically random token that is never stored and never published. If
/// this id were the secret, every administrator who could list invitations could
/// accept anybody's.
/// </para>
/// </remarks>
public readonly record struct TenantInvitationId
{
    private TenantInvitationId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static TenantInvitationId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">The value is empty.</exception>
    public static TenantInvitationId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Tenant invitation id must not be an empty GUID.")
            {
                Code = "invitations.id.invalid",
            };
        }

        return new TenantInvitationId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
