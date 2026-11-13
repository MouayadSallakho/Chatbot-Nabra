namespace OmniAI.Domain.Customers;

/// <summary>
/// A human-friendly label a colleague chose for a customer.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A label, never an identity key.</strong> Nothing resolves, matches,
/// deduplicates or merges on this value, and nothing ever may: two people called
/// "Sam" are two people, and a platform that decided otherwise would silently
/// join two strangers' conversation histories. Identity comes from the transport
/// relationship or from an administrator saying so, and from nowhere else.
/// </para>
/// <para>
/// <strong>Optional, because most customers do not have one.</strong> An
/// anonymous website visitor has no name to record, and inventing one — from the
/// first thing they typed, from a provider profile, from anything — would be the
/// platform asserting a fact about a real person that nobody checked.
/// </para>
/// <para>
/// It is untrusted text. It is bounded, single-line and free of control
/// characters so it cannot corrupt a list, a log or an export, and every surface
/// that shows it escapes it.
/// </para>
/// </remarks>
public sealed record CustomerDisplayName
{
    /// <summary>
    /// Maximum length, in UTF-16 code units, measured after trimming.
    /// </summary>
    /// <remarks>
    /// The same 120 as <c>AiAgentName</c>, and for the same reason: it is a short
    /// label that appears in lists and cards where anything longer is truncated by
    /// the interface anyway. It is comfortably above any real person's name,
    /// including long non-Latin forms and honorifics.
    /// </remarks>
    public const int MaxLength = 120;

    private CustomerDisplayName(string value) => Value = value;

    /// <summary>The normalised label.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and normalises a label. Surrounding whitespace is trimmed;
    /// nothing else is altered, so a value that breaks a rule is rejected rather
    /// than quietly repaired.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// Blank, longer than <see cref="MaxLength"/>, or containing a control
    /// character.
    /// </exception>
    public static CustomerDisplayName Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("A customer name must not be blank.")
            {
                Code = "customers.display_name.required",
            };
        }

        string normalised = value.Trim();

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A customer name must be at most {MaxLength} characters; "
                + $"the supplied name has {normalised.Length}. It is not truncated.")
            {
                Code = "customers.display_name.too_long",
            };
        }

        // Including line breaks and tabs: this is a single-line label, and
        // interior formatting has no meaning in one. A name that arrives with a
        // newline in it is a paste accident, and storing it would break every
        // list that renders one row per customer.
        foreach (char character in normalised)
        {
            if (char.IsControl(character))
            {
                throw new DomainValidationException(
                    "A customer name must not contain control characters.")
                {
                    Code = "customers.display_name.control_characters",
                };
            }
        }

        return new CustomerDisplayName(normalised);
    }

    /// <summary>
    /// Reads a label that may be absent, answering <see langword="null"/> for
    /// nothing at all.
    /// </summary>
    /// <remarks>
    /// Blank and absent are the same thing here — clearing the field is how a
    /// colleague removes a label they should not have written — so a caller does
    /// not have to decide which of the two an empty form field meant.
    /// </remarks>
    public static CustomerDisplayName? CreateOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Create(value);

    public override string ToString() => Value;
}
