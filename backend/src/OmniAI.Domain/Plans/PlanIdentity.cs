using System.Globalization;

namespace OmniAI.Domain.Plans;

/// <summary>The identity of one platform plan.</summary>
/// <remarks>
/// UUIDv7 like every other identity here, so inserts stay ordered and an index
/// does not fragment.
/// </remarks>
public readonly record struct PlanId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static PlanId New() => new(Guid.CreateVersion7());

    public static PlanId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("A plan id cannot be empty.")
                {
                    Code = "plans.id.invalid",
                }
            : new PlanId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// What a plan is called in configuration and in code.
/// </summary>
/// <remarks>
/// <para>
/// The stable handle. A deployment names its default plan by code, tests name
/// theirs by code, and the backfill names the legacy one by code — so the code is
/// the thing that must not drift, and it is what the unique index protects.
/// </para>
/// <para>
/// Deliberately not the display name. Renaming "Starter" to "Essentials" is a
/// marketing decision; it must not silently repoint a deployment's configured
/// default at a different set of limits.
/// </para>
/// </remarks>
public readonly record struct PlanCode
{
    public const int MaxLength = 64;

    private PlanCode(string value) => Value = value;

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Reads a code, normalising case and surrounding space.</summary>
    /// <remarks>
    /// Lower-cased on the way in so <c>Starter</c> and <c>starter</c> cannot both
    /// exist and mean different things. Restricted to letters, digits and hyphens
    /// because a code travels through configuration keys and command lines.
    /// </remarks>
    public static PlanCode Create(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("A plan needs a code.")
            {
                Code = "plans.code.invalid",
            };
        }

        if (trimmed.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A plan code cannot be longer than {MaxLength} characters.")
            {
                Code = "plans.code.invalid",
            };
        }

        foreach (char character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                throw new DomainValidationException(
                    "A plan code may contain only letters, digits and hyphens.")
                {
                    Code = "plans.code.invalid",
                };
            }
        }

        return new PlanCode(trimmed);
    }

    public override string ToString() => Value;
}

/// <summary>What a plan is called on a screen.</summary>
public readonly record struct PlanDisplayName
{
    public const int MaxLength = 128;

    private PlanDisplayName(string value) => Value = value;

    public string Value { get; }

    public static PlanDisplayName Create(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("A plan needs a display name.")
            {
                Code = "plans.name.invalid",
            };
        }

        return trimmed.Length > MaxLength
            ? throw new DomainValidationException(
                $"A plan name cannot be longer than {MaxLength} characters.")
                {
                    Code = "plans.name.invalid",
                }
            : new PlanDisplayName(trimmed);
    }

    public override string ToString() => Value;
}
