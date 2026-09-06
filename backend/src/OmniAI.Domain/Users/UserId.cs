using System.Globalization;

namespace OmniAI.Domain.Users;

/// <summary>
/// The identity of a person acting on the platform.
/// </summary>
/// <remarks>
/// <strong>This is an identifier, not an account.</strong> Credentials --
/// password hashes, security stamps, lockout counters, confirmation state --
/// belong to the identity provider in Infrastructure and never appear in the
/// Domain. What the Domain needs, and all it needs, is a way to say <em>which
/// person</em> a membership belongs to, and that is a value.
/// <para>
/// Modelled exactly like <c>TenantId</c> and <c>AiAgentId</c>: a readonly record
/// struct over a <see cref="Guid"/> with a private constructor, so the only way
/// to obtain one is through a factory that has already validated it. A bare
/// <c>Guid</c> parameter would be assignable from any other id in the system;
/// this one is not, and the compiler enforces it.
/// </para>
/// <para>
/// <strong>UUIDv7, like every other identifier here.</strong> Sequential integer
/// account ids leak how many accounts exist and how fast they are being created,
/// and make one account's id guessable from another's. Version 7 keeps the
/// time-ordered insert locality that makes b-tree indexes behave without
/// carrying a count.
/// </para>
/// </remarks>
public readonly record struct UserId
{
    private UserId(Guid value) => Value = value;

    /// <summary>The underlying value, for persistence and transport.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default, unusable value.</summary>
    /// <remarks>
    /// Reachable only through <c>default(UserId)</c>, which the factories cannot
    /// produce. It exists so a caller holding a struct it did not create can ask.
    /// </remarks>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Allocates a new identity.</summary>
    public static UserId New() => new(Guid.CreateVersion7());

    /// <summary>Rehydrates an identity from a stored or transported value.</summary>
    /// <exception cref="DomainValidationException">
    /// The value is the empty GUID. Rejected rather than accepted as "no user":
    /// an empty id that flowed into a membership check would compare equal to
    /// another empty id and grant access to whoever produced it.
    /// </exception>
    public static UserId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("User id must not be an empty GUID.");
        }

        return new UserId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
