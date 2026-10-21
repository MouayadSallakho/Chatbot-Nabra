using System.Globalization;
using OmniAI.Application.Ai.Usage;
using OmniAI.Domain;
using OmniAI.Domain.Ai;

namespace OmniAI.Infrastructure.Ai.Usage;

/// <summary>
/// One configured rate, as an operator writes it.
/// </summary>
/// <remarks>
/// Plain settable properties because this is bound from configuration. It is
/// validated into a <see cref="AiModelRates"/> once, at startup, so a typo is a
/// deployment that refuses to start rather than a cost that is quietly wrong.
/// </remarks>
public sealed class AiModelRateOptions
{
    /// <summary>The provider, by name. <c>OpenAi</c> today.</summary>
    public string Provider { get; set; } = nameof(AiProvider.OpenAi);

    /// <summary>The model, exactly as the provider spells it.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary><c>TextGeneration</c> or <c>Embedding</c>.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>ISO 4217, three letters.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>When this rate began to apply, in UTC.</summary>
    public DateTimeOffset EffectiveFrom { get; set; }

    public decimal InputPerMillion { get; set; }

    public decimal? CachedInputPerMillion { get; set; }

    public decimal? OutputPerMillion { get; set; }
}

/// <summary>
/// What this deployment was told the providers charge.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Configuration, and nothing else.</strong> Nothing here calls a
/// provider to ask a price, reads a web page, or guesses from a similar model.
/// The catalogue holds what an operator wrote down, and a model that is not in it
/// is unrated — reported as such rather than priced at a number somebody would
/// later have to defend.
/// </para>
/// <para>
/// <strong>Rates are versioned by date and never replaced.</strong> A price
/// change is another entry with a later effective date. Asking for a rate "as
/// at" an instant returns the latest one that had already taken effect, which is
/// what makes a call made in March still cost March's price in June.
/// </para>
/// <para>
/// Not a secret. A published rate card in configuration needs no protection, and
/// treating it as sensitive would only make it harder to keep current.
/// </para>
/// </remarks>
internal sealed class AiProviderPricingCatalog : IAiProviderPricing
{
    private readonly IReadOnlyDictionary<RateKey, IReadOnlyList<AiModelRates>> _rates;

    private AiProviderPricingCatalog(
        IReadOnlyDictionary<RateKey, IReadOnlyList<AiModelRates>> rates) =>
        _rates = rates;

    /// <summary>A deployment that has configured no prices. Everything is unrated.</summary>
    public static AiProviderPricingCatalog Empty { get; } =
        new(new Dictionary<RateKey, IReadOnlyList<AiModelRates>>());

    /// <summary>
    /// Reads and validates the configured rates.
    /// </summary>
    /// <remarks>
    /// Validated eagerly, at startup. A misspelled model, an unknown operation or
    /// a negative rate should stop a deployment from starting, not surface a
    /// month later as a cost report nobody can explain.
    /// </remarks>
    /// <param name="entries">The configured rates.</param>
    /// <param name="includeLocalStandIn">
    /// <para>
    /// Adds the local AI stand-in's built-in zero rate for both operations
    /// (Phase 37.2). The composition root passes <see langword="true"/> only when
    /// it registers the stand-in, so without the stand-in there is no rate for
    /// <see cref="AiProvider.DevelopmentStandIn"/> at all.
    /// </para>
    /// <para>
    /// It prices the stand-in's word counts, which are not tokens, at nothing.
    /// It changes no configured rate and not the rule that a call reporting no
    /// usage is unrated.
    /// </para>
    /// </param>
    /// <exception cref="InvalidOperationException">An entry is not usable.</exception>
    public static AiProviderPricingCatalog Create(
        IReadOnlyList<AiModelRateOptions>? entries,
        bool includeLocalStandIn = false)
    {
        if ((entries is null || entries.Count == 0) && !includeLocalStandIn)
        {
            return Empty;
        }

        Dictionary<RateKey, List<AiModelRates>> byKey = [];

        IEnumerable<AiModelRates> all = (entries ?? []).Select(Read);

        if (includeLocalStandIn)
        {
            all = all.Concat(LocalStandInRates());
        }

        foreach (AiModelRates rates in all)
        {
            RateKey key = new(rates.Provider, rates.Model.Value, rates.Operation);

            if (!byKey.TryGetValue(key, out List<AiModelRates>? versions))
            {
                versions = [];
                byKey[key] = versions;
            }

            if (versions.Any(existing => existing.EffectiveFrom == rates.EffectiveFrom))
            {
                throw new InvalidOperationException(
                    $"Two rates for {rates.Model} ({rates.Operation}) take effect at the same "
                    + "instant. Which one applies would be arbitrary, so neither is accepted.");
            }

            versions.Add(rates);
        }

        // Newest first, so a lookup takes the first entry that had already taken
        // effect rather than scanning the lot.
        return new AiProviderPricingCatalog(
            byKey.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<AiModelRates>)
                    [.. pair.Value.OrderByDescending(rates => rates.EffectiveFrom)]));
    }

    public AiModelRates? Find(
        AiProvider provider,
        ModelName model,
        AiOperationType operationType,
        DateTimeOffset at)
    {
        if (!_rates.TryGetValue(new RateKey(provider, model.Value, operationType),
                out IReadOnlyList<AiModelRates>? versions))
        {
            // No entry for this model. Unrated, which is an ordinary answer.
            return null;
        }

        DateTimeOffset instant = at.ToUniversalTime();

        // The latest rate that had already taken effect. A call made before any
        // configured rate began is unrated rather than priced at a rate that did
        // not exist yet.
        return versions.FirstOrDefault(rates => rates.EffectiveFrom <= instant);
    }

    /// <summary>The local AI stand-in's zero rate, for both operations.</summary>
    /// <remarks>
    /// In force from the Unix epoch, so every stand-in call is rated. USD like
    /// the configured default, so a local workspace's totals are not split by
    /// currency.
    /// </remarks>
    private static AiModelRates[] LocalStandInRates()
    {
        ModelName model = ModelName.Create(StandIn.LocalAiStandInOptions.Model);
        CurrencyCode currency = CurrencyCode.Create("USD");

        return
        [
            AiModelRates.Create(
                AiProvider.DevelopmentStandIn,
                model,
                AiOperationType.TextGeneration,
                currency,
                DateTimeOffset.UnixEpoch,
                inputPerMillion: 0m,
                cachedInputPerMillion: 0m,
                outputPerMillion: 0m),
            AiModelRates.Create(
                AiProvider.DevelopmentStandIn,
                model,
                AiOperationType.Embedding,
                currency,
                DateTimeOffset.UnixEpoch,
                inputPerMillion: 0m),
        ];
    }

    private static AiModelRates Read(AiModelRateOptions entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!Enum.TryParse(entry.Provider, ignoreCase: true, out AiProvider provider))
        {
            throw new InvalidOperationException(
                $"'{entry.Provider}' is not a provider this platform calls.");
        }

        if (provider == AiProvider.DevelopmentStandIn)
        {
            // Its zero rate is built in and exists only while the stand-in is
            // registered; a configured one would outlive it.
            throw new InvalidOperationException(
                $"'{entry.Provider}' cannot be priced in configuration. The local AI stand-in's "
                + "zero rate is built in and applies only while the stand-in is running.");
        }

        if (!Enum.TryParse(entry.Operation, ignoreCase: true, out AiOperationType operation))
        {
            throw new InvalidOperationException(
                $"'{entry.Operation}' is not an operation this platform accounts for. "
                + $"Use {nameof(AiOperationType.TextGeneration)} or "
                + $"{nameof(AiOperationType.Embedding)}.");
        }

        if (entry.EffectiveFrom == default)
        {
            throw new InvalidOperationException(
                $"The rate for '{entry.Model}' must say when it took effect. Without that, a "
                + "historical call cannot be rated reproducibly.");
        }

        try
        {
            return AiModelRates.Create(
                provider,
                ModelName.Create(entry.Model),
                operation,
                CurrencyCode.Create(entry.Currency),
                entry.EffectiveFrom,
                entry.InputPerMillion,
                entry.CachedInputPerMillion,
                entry.OutputPerMillion);
        }
        catch (DomainValidationException invalid)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The configured rate for '{entry.Model}' is not usable: {invalid.Message}"),
                invalid);
        }
    }

    /// <summary>What makes one rate series distinct from another.</summary>
    private readonly record struct RateKey(AiProvider Provider, string Model, AiOperationType Operation);
}
