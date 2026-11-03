using System.Globalization;

namespace OmniAI.Domain.WhatsApp;

/// <summary>
/// A customer's WhatsApp identity, as Meta reports it.
/// </summary>
/// <remarks>
/// <strong>This is personal data, and the type says so.</strong> A <c>wa_id</c>
/// is a phone number in all but name — it identifies a real person, it is the
/// same value across every business they message, and it is enough to contact
/// them. So it is never logged raw, never published to a public transport, and
/// masked by default wherever staff see it.
/// <para>
/// It is stored because sending a reply requires it. That is the whole reason,
/// and nothing else about the customer is kept: no profile name, no push name,
/// no locale.
/// </para>
/// </remarks>
public readonly record struct WhatsAppCustomerId
{
    /// <summary>The longest value this platform will store.</summary>
    public const int MaxLength = 32;

    private WhatsAppCustomerId(string value) => Value = value;

    /// <summary>
    /// The raw identifier.
    /// </summary>
    /// <remarks>
    /// Reached for only when sending to Meta or comparing to a stored mapping.
    /// Anything that displays or logs uses <see cref="Masked"/>.
    /// </remarks>
    public string Value { get; }

    /// <summary>
    /// What a person is shown: the last four digits, and nothing else.
    /// </summary>
    /// <remarks>
    /// Enough for a colleague to tell two conversations apart or to match one
    /// against a customer on the phone, and not enough to contact somebody or to
    /// identify them from a screenshot. A very short value is masked entirely
    /// rather than revealed by a rule that assumed it was longer.
    /// </remarks>
    public string Masked =>
        Value.Length <= 4
            ? new string('•', Value.Length)
            : string.Create(
                CultureInfo.InvariantCulture,
                $"••••{Value.AsSpan(Value.Length - 4)}");

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">Blank, too long, or not digits.</exception>
    public static WhatsAppCustomerId Create(string? value) =>
        new(MetaIdentifier.Require(value, MaxLength, "WhatsApp customer id"));

    /// <summary>
    /// Reads one from a webhook, answering <see langword="false"/> rather than
    /// throwing.
    /// </summary>
    public static bool TryCreate(string? value, out WhatsAppCustomerId customerId)
    {
        if (MetaIdentifier.IsWellFormed(value, MaxLength))
        {
            customerId = new WhatsAppCustomerId(value!.Trim());

            return true;
        }

        customerId = default;

        return false;
    }

    /// <summary>
    /// Masked, so an accidental interpolation into a log line discloses nothing.
    /// </summary>
    /// <remarks>
    /// The single most useful line in this file. Every logger, every exception
    /// message and every string interpolation calls this by default, so the raw
    /// value reaches a log only if somebody deliberately wrote <c>.Value</c> —
    /// which is a thing a reviewer can see.
    /// </remarks>
    public override string ToString() => Masked;
}

/// <summary>
/// The identifier Meta gives a message, inbound or outbound.
/// </summary>
/// <remarks>
/// A <c>wamid</c>. For an inbound message it is the idempotency key: Meta
/// redelivers webhooks, so the same value arriving twice must produce one
/// customer message and at most one AI attempt. For an outbound message it is
/// what later status webhooks are matched against.
/// <para>
/// Not digits, unlike the account identifiers — it is an opaque base64-ish
/// string — so it is validated only for length and printable content.
/// </para>
/// </remarks>
public readonly record struct ProviderMessageId
{
    /// <summary>The longest value this platform will store.</summary>
    /// <remarks>
    /// Meta's are around sixty characters. Two hundred and fifty-six leaves room
    /// for a format change without letting a malformed payload write an
    /// unbounded string into an indexed column.
    /// </remarks>
    public const int MaxLength = 256;

    private ProviderMessageId(string value) => Value = value;

    /// <summary>The underlying value.</summary>
    public string Value { get; }

    /// <summary>Whether this is the default value rather than a real id.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Reads an identifier supplied from outside.</summary>
    /// <exception cref="DomainValidationException">Blank, too long, or unprintable.</exception>
    public static ProviderMessageId Create(string? value) =>
        TryCreate(value, out ProviderMessageId id)
            ? id
            : throw new DomainValidationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A provider message id must be 1 to {MaxLength} printable characters."))
                {
                    Code = "whatsapp.provider_id.invalid",
                };

    /// <summary>
    /// Reads one from a webhook, answering <see langword="false"/> rather than
    /// throwing.
    /// </summary>
    public static bool TryCreate(string? value, out ProviderMessageId messageId)
    {
        messageId = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return false;
        }

        foreach (char character in trimmed)
        {
            // Printable ASCII only. A control character in an identifier is
            // either a mistake or an attempt to break a log line in half.
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        messageId = new ProviderMessageId(trimmed);

        return true;
    }

    public override string ToString() => Value;
}
