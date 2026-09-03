namespace OmniAI.Domain.Tenants;

/// <summary>
/// Display name of a tenant business, for example <c>Acme Trading L.L.C.</c>
/// </summary>
/// <remarks>
/// A validated type rather than a bare <see cref="string"/>, so the "non-blank,
/// trimmed, bounded" rule is enforced once at construction instead of at every
/// call site that happens to remember it.
/// </remarks>
public sealed record TenantName
{
    /// <summary>
    /// Maximum length, in UTF-16 code units, matching how .NET measures
    /// <see cref="string.Length"/> and how a database sizes an <c>nvarchar</c>.
    /// </summary>
    /// <remarks>
    /// 200 comfortably fits a full legal or trading name -- including long
    /// non-Latin names and corporate suffixes -- while staying well inside the
    /// index key limits of every mainstream database. It bounds display, log and
    /// storage size without being a limit a real business would ever reach.
    /// </remarks>
    public const int MaxLength = 200;

    private TenantName(string value) => Value = value;

    /// <summary>The normalised name.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and normalises a display name. Surrounding whitespace is
    /// trimmed; nothing else is altered, so a name that breaks a rule is
    /// rejected rather than quietly repaired.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The value is null, blank, longer than <see cref="MaxLength"/>, or contains
    /// a control character.
    /// </exception>
    public static TenantName Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("Tenant name is required.");
        }

        string normalised = value.Trim();

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"Tenant name must be at most {MaxLength} characters; the supplied name has {normalised.Length}.");
        }

        // Control characters serve no purpose in a display name and would corrupt
        // logs, headers and rendered output.
        foreach (char character in normalised)
        {
            if (char.IsControl(character))
            {
                throw new DomainValidationException("Tenant name must not contain control characters.");
            }
        }

        return new TenantName(normalised);
    }

    public override string ToString() => Value;
}
