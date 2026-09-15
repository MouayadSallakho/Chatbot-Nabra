namespace OmniAI.Domain.Ai;

/// <summary>
/// A model identifier, as the provider spells it.
/// </summary>
/// <remarks>
/// <para>
/// Bounded and trimmed, because it is grouped by and displayed. It carries no
/// meaning to this platform beyond identity: no parsing of families, versions or
/// capabilities, which would be a second model registry that drifts from the
/// provider's.
/// </para>
/// <para>
/// Compared case-sensitively and stored as given. A provider that answers with a
/// dated variant of what was asked for — <c>gpt-x-2026-01-01</c> against a
/// requested <c>gpt-x</c> — is telling us something worth keeping, which is why
/// requested and resolved names are recorded separately rather than one
/// overwriting the other.
/// </para>
/// </remarks>
public readonly record struct ModelName
{
    /// <summary>Long enough for any published identifier, bounded against abuse.</summary>
    public const int MaxLength = 128;

    private ModelName(string value) => Value = value;

    public string Value { get; }

    /// <summary>Whether this is the default value rather than a real name.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <exception cref="DomainValidationException">Blank, or past the bound.</exception>
    public static ModelName Create(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("A model name must not be blank.");
        }

        if (trimmed.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A model name must be at most {MaxLength} characters; this one has "
                + $"{trimmed.Length}.");
        }

        return new ModelName(trimmed);
    }

    /// <summary>Reads a name that may be absent, without inventing one.</summary>
    public static ModelName? CreateOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Create(value);

    public override string ToString() => Value;
}
