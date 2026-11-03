using System.Globalization;

namespace OmniAI.Domain.WhatsApp;

/// <summary>
/// The identity of one workspace's WhatsApp connection.
/// </summary>
/// <remarks>
/// A UUIDv7 like every other identifier in this model. It is the administrative
/// identity and appears only in admin routes; nothing Meta sends ever contains
/// it, and nothing routes an inbound webhook by it.
/// </remarks>
public readonly record struct WhatsAppConnectionId
{
    private WhatsAppConnectionId(Guid value) => Value = value;

    /// <summary>The underlying value.</summary>
    public Guid Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>A new identifier.</summary>
    public static WhatsAppConnectionId New() => new(Guid.CreateVersion7());

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">The value is empty.</exception>
    public static WhatsAppConnectionId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new DomainValidationException(
                "WhatsApp connection id must not be an empty GUID.")
            {
                Code = "whatsapp.id.invalid",
            };
        }

        return new WhatsAppConnectionId(value);
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
