using System.Globalization;

namespace OmniAI.Domain.Customers;

/// <summary>
/// Identity of a <see cref="Customer"/> — the person a workspace is talking to.
/// </summary>
/// <remarks>
/// <para>
/// Follows the accepted <c>TenantId</c> design: a distinct type, and UUID version
/// 7 so generated keys stay time-ordered and insert locally in an index.
/// </para>
/// <para>
/// <strong>Deliberately not <c>WhatsAppCustomerId</c>, and never interchangeable
/// with it.</strong> That type is a phone number Meta gave us; this one is the
/// platform's own name for a person, valid across every channel and meaningful
/// after the phone number changes hands. Two distinct types is what stops a
/// provider identifier being passed where a canonical identity is expected.
/// </para>
/// </remarks>
public readonly record struct CustomerId
{
    private CustomerId(Guid value) => Value = value;

    /// <summary>The underlying identifier.</summary>
    public Guid Value { get; }

    /// <summary>True for the uninitialised <c>default</c> value.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Generates a new identity.</summary>
    public static CustomerId New() => new(Guid.CreateVersion7());

    /// <summary>Wraps an existing identifier.</summary>
    /// <exception cref="DomainValidationException">The identifier is empty.</exception>
    public static CustomerId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException("Customer id must not be an empty GUID.")
            {
                Code = "customers.id.invalid",
            };
        }

        return new CustomerId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
