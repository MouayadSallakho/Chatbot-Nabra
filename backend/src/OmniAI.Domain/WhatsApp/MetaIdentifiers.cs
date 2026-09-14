using System.Globalization;

namespace OmniAI.Domain.WhatsApp;

/// <summary>
/// A WhatsApp Business Account identifier, as Meta issues it.
/// </summary>
/// <remarks>
/// <strong>A string, not a number.</strong> Meta's identifiers are decimal
/// digits today and are documented as opaque; parsing one into a long would
/// silently truncate the day they exceed 64 bits, and would turn a leading zero
/// into a different account. It is stored and compared exactly as received.
/// <para>
/// It is not a secret. It appears in an administrator's own screens and in
/// Meta's dashboards, and on its own it grants nothing — the access token is the
/// credential, and it lives somewhere else entirely.
/// </para>
/// </remarks>
public readonly record struct WabaId
{
    /// <summary>The longest value this platform will store.</summary>
    /// <remarks>
    /// Meta's identifiers are around sixteen digits. Sixty-four is generous
    /// enough to outlast a format change and short enough that a pasted
    /// paragraph is refused rather than stored.
    /// </remarks>
    public const int MaxLength = 64;

    private WabaId(string value) => Value = value;

    /// <summary>The underlying value.</summary>
    public string Value { get; }

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">
    /// Blank, too long, or containing anything but digits.
    /// </exception>
    public static WabaId Create(string? value) =>
        new(MetaIdentifier.Require(value, MaxLength, "WhatsApp Business Account id"));

    public override string ToString() => Value;
}

/// <summary>
/// The identifier of the phone number a workspace sends and receives on.
/// </summary>
/// <remarks>
/// <strong>This is the routing key for every inbound webhook.</strong> Meta puts
/// it in <c>value.metadata.phone_number_id</c>, and it is the only field in a
/// webhook this platform trusts to decide which workspace a message belongs to —
/// because it is the only one Meta itself controls. Everything else in the
/// payload is, from this platform's point of view, attacker-influenced content
/// that merely arrived over an authenticated channel.
/// <para>
/// It is unique across the whole deployment: two workspaces claiming one phone
/// number would make routing ambiguous, and "ambiguous" here means one
/// workspace's customers reaching another workspace's inbox.
/// </para>
/// </remarks>
public readonly record struct PhoneNumberId
{
    /// <summary>The longest value this platform will store.</summary>
    public const int MaxLength = 64;

    private PhoneNumberId(string value) => Value = value;

    /// <summary>The underlying value.</summary>
    public string Value { get; }

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">
    /// Blank, too long, or containing anything but digits.
    /// </exception>
    public static PhoneNumberId Create(string? value) =>
        new(MetaIdentifier.Require(value, MaxLength, "phone number id"));

    /// <summary>
    /// Reads one from a webhook, answering <see langword="false"/> rather than
    /// throwing.
    /// </summary>
    /// <remarks>
    /// A malformed identifier in a webhook is not an exception: the request was
    /// signed, so it really did come from Meta, and the right answer is to
    /// acknowledge it and route it nowhere. Throwing would turn a shrug into a
    /// 500 and invite Meta to redeliver it for ever.
    /// </remarks>
    public static bool TryCreate(string? value, out PhoneNumberId phoneNumberId)
    {
        if (MetaIdentifier.IsWellFormed(value, MaxLength))
        {
            phoneNumberId = new PhoneNumberId(value!.Trim());

            return true;
        }

        phoneNumberId = default;

        return false;
    }

    public override string ToString() => Value;
}

/// <summary>
/// The shared rules for the identifiers Meta issues.
/// </summary>
internal static class MetaIdentifier
{
    internal static string Require(string? value, int maxLength, string what)
    {
        if (!IsWellFormed(value, maxLength))
        {
            throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {what} must be between 1 and {maxLength} digits."));
        }

        return value!.Trim();
    }

    internal static bool IsWellFormed(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();

        if (trimmed.Length == 0 || trimmed.Length > maxLength)
        {
            return false;
        }

        foreach (char character in trimmed)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
