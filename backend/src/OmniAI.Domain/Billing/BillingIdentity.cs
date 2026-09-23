using System.Globalization;

namespace OmniAI.Domain.Billing;

/// <summary>
/// The payment platforms this system can bill through.
/// </summary>
/// <remarks>
/// <strong>An enum, not a string, and deliberately not a synonym for Stripe.</strong>
/// Stripe is the first supported provider and currently the only one, but the
/// subscription domain is written against the concept rather than the company:
/// adding a second provider must be an adapter and a row here, not a rewrite of
/// how subscriptions work.
/// </remarks>
public enum BillingProvider
{
    Stripe = 1,
}

/// <summary>How often a commercial offer recurs.</summary>
/// <remarks>
/// Two values, and no more. Weekly, daily and custom intervals are not sold, so
/// they are not representable — an enum that could express them would have to be
/// handled everywhere a period is computed or displayed.
/// </remarks>
public enum BillingInterval
{
    Monthly = 1,
    Annual = 2,
}

/// <summary>The identity of one commercial offer.</summary>
public readonly record struct BillingOfferId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static BillingOfferId New() => new(Guid.CreateVersion7());

    public static BillingOfferId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("A billing offer id cannot be empty.")
            : new BillingOfferId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>The identity of one workspace subscription.</summary>
public readonly record struct WorkspaceSubscriptionId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static WorkspaceSubscriptionId New() => new(Guid.CreateVersion7());

    public static WorkspaceSubscriptionId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("A subscription id cannot be empty.")
            : new WorkspaceSubscriptionId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>The identity of one checkout attempt.</summary>
public readonly record struct BillingCheckoutIntentId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static BillingCheckoutIntentId New() => new(Guid.CreateVersion7());

    public static BillingCheckoutIntentId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("A checkout intent id cannot be empty.")
            : new BillingCheckoutIntentId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>The identity of one stored provider event.</summary>
public readonly record struct BillingEventId(Guid Value)
{
    public bool IsEmpty => Value == Guid.Empty;

    public static BillingEventId New() => new(Guid.CreateVersion7());

    public static BillingEventId From(Guid value) =>
        value == Guid.Empty
            ? throw new DomainValidationException("A billing event id cannot be empty.")
            : new BillingEventId(value);

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}

/// <summary>
/// The stable handle an offer is known by.
/// </summary>
/// <remarks>
/// What the browser sends when somebody chooses what to buy. Never a provider
/// price identifier: the customer's authority is an internal code this platform
/// controls, and the mapping to whatever Stripe calls it happens server-side.
/// </remarks>
public readonly record struct OfferCode
{
    public const int MaxLength = 64;

    private OfferCode(string value) => Value = value;

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public static OfferCode Create(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("An offer needs a code.");
        }

        if (trimmed.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"An offer code cannot be longer than {MaxLength} characters.");
        }

        foreach (char character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                throw new DomainValidationException(
                    "An offer code may contain only letters, digits and hyphens.");
            }
        }

        return new OfferCode(trimmed);
    }

    public override string ToString() => Value;
}

/// <summary>
/// An opaque identifier belonging to a billing provider.
/// </summary>
/// <remarks>
/// <para>
/// A customer id, a subscription id, a price id, a session id. This platform
/// never parses one, never derives meaning from its shape and never lets a
/// browser choose one: it is a reference handed back to the provider later.
/// </para>
/// <para>
/// It is not a secret — it identifies a record, it does not authorise anything —
/// but it is also not a customer's business, so the API keeps it out of ordinary
/// responses.
/// </para>
/// </remarks>
public readonly record struct ProviderReference
{
    public const int MaxLength = 255;

    private ProviderReference(string value) => Value = value;

    public string Value { get; }

    public bool IsEmpty => string.IsNullOrEmpty(Value);

    public static ProviderReference Create(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("A provider reference cannot be blank.");
        }

        return trimmed.Length > MaxLength
            ? throw new DomainValidationException(
                $"A provider reference cannot be longer than {MaxLength} characters.")
            : new ProviderReference(trimmed);
    }

    /// <summary>Reads an optional reference, where blank means absent.</summary>
    public static ProviderReference? CreateOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Create(value);

    public override string ToString() => Value;
}

/// <summary>
/// An amount of money, in the currency's smallest unit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Minor units as a whole number, never a floating-point major
/// amount.</strong> 19.99 is not representable in binary floating point, and a
/// price that drifts in the second decimal place is a price somebody will
/// eventually be charged wrongly. Storing 1999 makes the value exact and happens
/// to match what every payment provider expects on the wire.
/// </para>
/// <para>
/// How many minor units make a major one is a property of the currency, not of
/// this type, so nothing here divides by a hundred. Formatting belongs to
/// whatever is displaying it, which knows the reader's locale.
/// </para>
/// </remarks>
public readonly record struct Money
{
    private Money(long minorUnits, BillingCurrency currency)
    {
        MinorUnits = minorUnits;
        Currency = currency;
    }

    /// <summary>The amount, in the currency's smallest unit. Never negative.</summary>
    public long MinorUnits { get; }

    public BillingCurrency Currency { get; }

    public static Money Of(long minorUnits, BillingCurrency currency) =>
        minorUnits < 0
            ? throw new DomainValidationException("A price cannot be negative.")
            : new Money(minorUnits, currency);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{MinorUnits} {Currency}");
}

/// <summary>An ISO 4217 currency code. Three letters, upper case.</summary>
/// <remarks>
/// Its own type in this namespace rather than one borrowed from provider
/// accounting: what a workspace is charged and what this platform paid OpenAI are
/// unrelated facts that happen to share a shape, and coupling them would let a
/// change in one reach the other.
/// </remarks>
public readonly record struct BillingCurrency
{
    private BillingCurrency(string value) => Value = value;

    public string Value { get; }

    public static BillingCurrency Create(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim().ToUpperInvariant();

        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new DomainValidationException(
                "A currency must be a three-letter ISO 4217 code.");
        }

        return new BillingCurrency(trimmed);
    }

    public override string ToString() => Value;
}
