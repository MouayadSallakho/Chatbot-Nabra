using System.Globalization;

namespace OmniAI.Domain.Ai;

/// <summary>
/// A currency, as three letters.
/// </summary>
/// <remarks>
/// <strong>Carried explicitly, never assumed.</strong> Not every AI provider
/// bills in dollars, and a Domain that hard-coded USD would make the first
/// non-dollar provider a rewrite instead of a configuration entry.
/// </remarks>
public readonly record struct CurrencyCode
{
    private CurrencyCode(string value) => Value = value;

    /// <summary>The ISO 4217 code, upper case.</summary>
    public string Value { get; }

    /// <summary>Whether this is the default value rather than a real code.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Reads a code, refusing anything that is not three letters.</summary>
    public static CurrencyCode Create(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim().ToUpperInvariant();

        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new DomainValidationException(
                $"A currency must be a three-letter ISO 4217 code; '{value}' is not.");
        }

        return new CurrencyCode(trimmed);
    }

    public override string ToString() => Value;
}

/// <summary>
/// What one provider charges for one model, from one date.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Rates are configuration, not business logic.</strong> Nothing in this
/// platform knows "the current OpenAI price": it knows how to apply a rate it was
/// given. A price that moved is a new entry with a later
/// <see cref="EffectiveFrom"/>, which is what keeps yesterday's figures
/// reproducible.
/// </para>
/// <para>
/// <strong>Everything is <see langword="decimal"/>.</strong> Binary floating
/// point cannot represent a tenth of a cent exactly, and money that is out by a
/// rounding error is money nobody can reconcile.
/// </para>
/// <para>
/// Rates are per million tokens because that is the unit every provider
/// publishes; storing them per token would mean carrying a great many zeroes and
/// losing precision to no purpose.
/// </para>
/// </remarks>
public sealed record AiModelRates
{
    /// <summary>The scale rates are quoted at.</summary>
    public const decimal TokensPerUnit = 1_000_000m;

    private AiModelRates(
        AiProvider provider,
        ModelName model,
        AiOperationType operation,
        CurrencyCode currency,
        DateTimeOffset effectiveFrom,
        decimal inputPerMillion,
        decimal? cachedInputPerMillion,
        decimal? outputPerMillion)
    {
        Provider = provider;
        Model = model;
        Operation = operation;
        Currency = currency;
        EffectiveFrom = effectiveFrom;
        InputPerMillion = inputPerMillion;
        CachedInputPerMillion = cachedInputPerMillion;
        OutputPerMillion = outputPerMillion;
    }

    public AiProvider Provider { get; }

    public ModelName Model { get; }

    /// <summary>
    /// Which kind of operation these rates price.
    /// </summary>
    /// <remarks>
    /// Part of the key: the same provider prices generation and embedding
    /// differently, and one entry covering both would rate one of them wrongly.
    /// </remarks>
    public AiOperationType Operation { get; }

    public CurrencyCode Currency { get; }

    /// <summary>When these rates began to apply, in UTC.</summary>
    public DateTimeOffset EffectiveFrom { get; }

    /// <summary>Price of a million input tokens.</summary>
    public decimal InputPerMillion { get; }

    /// <summary>
    /// Price of a million cached input tokens, where the provider prices them
    /// separately.
    /// </summary>
    /// <remarks>
    /// Null means this model has no distinct cached rate. It does not mean free:
    /// see <see cref="Rate"/>, which refuses to price a call that reported cached
    /// tokens without one.
    /// </remarks>
    public decimal? CachedInputPerMillion { get; }

    /// <summary>
    /// Price of a million output tokens. Null for an embedding model, which
    /// produces none.
    /// </summary>
    public decimal? OutputPerMillion { get; }

    public static AiModelRates Create(
        AiProvider provider,
        ModelName model,
        AiOperationType operation,
        CurrencyCode currency,
        DateTimeOffset effectiveFrom,
        decimal inputPerMillion,
        decimal? cachedInputPerMillion = null,
        decimal? outputPerMillion = null)
    {
        if (model.IsEmpty)
        {
            throw new DomainValidationException("A rate must name the model it prices.");
        }

        if (currency.IsEmpty)
        {
            throw new DomainValidationException("A rate must name its currency.");
        }

        RequireNotNegative(inputPerMillion, "input");
        RequireNotNegative(cachedInputPerMillion, "cached input");
        RequireNotNegative(outputPerMillion, "output");

        if (operation == AiOperationType.TextGeneration && outputPerMillion is null)
        {
            throw new DomainValidationException(
                "A text generation rate must price output tokens; generation produces them.");
        }

        return new AiModelRates(
            provider,
            model,
            operation,
            currency,
            effectiveFrom.ToUniversalTime(),
            inputPerMillion,
            cachedInputPerMillion,
            outputPerMillion);
    }

    /// <summary>
    /// What this call cost, or <see langword="null"/> when it cannot be said
    /// exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>All of it or none of it.</strong> A call is rated only when every
    /// rate its reported usage needs is present. Pricing the input and quietly
    /// omitting the output would produce a number that looks like a cost, is
    /// smaller than the truth, and has nothing on screen to say so.
    /// </para>
    /// <para>
    /// <strong>Reasoning tokens are not added.</strong> They are already inside
    /// the output count under the current contract, and charging them again would
    /// bill the same tokens twice.
    /// </para>
    /// </remarks>
    public decimal? Rate(ProviderTokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (!usage.IsReported)
        {
            // Nothing was reported, so there is nothing to price. An unreported
            // call is unrated, never free.
            return null;
        }

        decimal cost = 0m;

        if (usage.InputTokens is long input)
        {
            long cached = usage.CachedInputTokens ?? 0;

            if (cached > 0)
            {
                if (CachedInputPerMillion is not decimal cachedRate)
                {
                    // The provider charged a cached rate this catalogue does not
                    // have. Guessing it is the standard rate would overstate the
                    // cost; guessing zero would understate it.
                    return null;
                }

                cost += Per(cached, cachedRate);
            }

            cost += Per(input - cached, InputPerMillion);
        }

        if (usage.OutputTokens is long output && output > 0)
        {
            if (OutputPerMillion is not decimal outputRate)
            {
                return null;
            }

            cost += Per(output, outputRate);
        }

        return cost;
    }

    private static decimal Per(long tokens, decimal ratePerMillion) =>
        tokens * ratePerMillion / TokensPerUnit;

    private static void RequireNotNegative(decimal? value, string what)
    {
        if (value is decimal amount && amount < 0)
        {
            throw new DomainValidationException(string.Create(
                CultureInfo.InvariantCulture,
                $"A {what} rate cannot be negative; {amount} was given."));
        }
    }
}
