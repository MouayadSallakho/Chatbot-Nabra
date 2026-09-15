using OmniAI.Domain.Ai;
using OmniAI.Infrastructure.Ai.Usage;

namespace OmniAI.Infrastructure.IntegrationTests.Ai;

/// <summary>
/// The configured rate card.
/// </summary>
/// <remarks>
/// <strong>Every rate in this file is invented.</strong> Nothing in this
/// repository claims to know what any provider charges; the catalogue holds what
/// an operator configured, and these numbers are chosen to be easy to check by
/// hand.
/// </remarks>
public sealed class AiProviderPricingCatalogTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_deployment_that_configured_nothing_rates_nothing()
    {
        // An ordinary deployment, not a broken one. Every call is then unrated,
        // which is reported honestly rather than shown as free.
        Assert.Null(AiProviderPricingCatalog.Create(null).Find(
            AiProvider.OpenAi,
            ModelName.Create("anything"),
            AiOperationType.TextGeneration,
            Noon));
    }

    [Fact]
    public void A_configured_rate_is_found_for_its_own_model_and_operation()
    {
        AiProviderPricingCatalog catalogue = Catalogue(Entry("model-a", "TextGeneration", 2m, output: 8m));

        Assert.NotNull(catalogue.Find(
            AiProvider.OpenAi, ModelName.Create("model-a"), AiOperationType.TextGeneration, Noon));

        // Not for another model...
        Assert.Null(catalogue.Find(
            AiProvider.OpenAi, ModelName.Create("model-b"), AiOperationType.TextGeneration, Noon));

        // ...and not for a different kind of operation. Generation and embedding
        // are different products at different prices.
        Assert.Null(catalogue.Find(
            AiProvider.OpenAi, ModelName.Create("model-a"), AiOperationType.Embedding, Noon));
    }

    // ------------------------------------------------------------- versioning

    [Fact]
    public void A_call_is_rated_with_whatever_was_in_force_when_it_was_dispatched()
    {
        AiProviderPricingCatalog catalogue = Catalogue(
            Entry("model-a", "TextGeneration", 2m, output: 8m, from: Noon.AddYears(-1)),
            Entry("model-a", "TextGeneration", 5m, output: 20m, from: Noon.AddDays(-1)));

        // A call made before the rise pays the old price, forever.
        Assert.Equal(2m, Find(catalogue, Noon.AddDays(-30))?.InputPerMillion);

        // One made after pays the new one.
        Assert.Equal(5m, Find(catalogue, Noon)?.InputPerMillion);
    }

    [Fact]
    public void A_call_made_before_any_configured_rate_is_unrated()
    {
        AiProviderPricingCatalog catalogue = Catalogue(
            Entry("model-a", "TextGeneration", 2m, output: 8m, from: Noon));

        // Pricing it at a rate that did not exist yet would be a guess.
        Assert.Null(Find(catalogue, Noon.AddDays(-1)));
    }

    [Fact]
    public void Two_rates_taking_effect_at_the_same_instant_are_refused()
    {
        // Which one applies would be arbitrary, and an arbitrary price is worse
        // than a deployment that refuses to start.
        Assert.Throws<InvalidOperationException>(() => Catalogue(
            Entry("model-a", "TextGeneration", 2m, output: 8m),
            Entry("model-a", "TextGeneration", 3m, output: 9m)));
    }

    // ------------------------------------------------------------- validation

    [Fact]
    public void An_unknown_provider_stops_the_deployment_rather_than_being_ignored()
    {
        AiModelRateOptions entry = Entry("model-a", "TextGeneration", 2m, output: 8m);

        entry.Provider = "SomeOtherCompany";

        Assert.Throws<InvalidOperationException>(() => Catalogue(entry));
    }

    [Fact]
    public void An_unknown_operation_stops_the_deployment()
    {
        AiModelRateOptions entry = Entry("model-a", "Transcription", 2m, output: 8m);

        Assert.Throws<InvalidOperationException>(() => Catalogue(entry));
    }

    [Fact]
    public void A_rate_with_no_effective_date_stops_the_deployment()
    {
        AiModelRateOptions entry = Entry("model-a", "TextGeneration", 2m, output: 8m);

        entry.EffectiveFrom = default;

        // Without it, a historical call cannot be rated reproducibly.
        Assert.Throws<InvalidOperationException>(() => Catalogue(entry));
    }

    [Fact]
    public void A_negative_rate_stops_the_deployment()
    {
        AiModelRateOptions entry = Entry("model-a", "TextGeneration", -1m, output: 8m);

        Assert.Throws<InvalidOperationException>(() => Catalogue(entry));
    }

    [Fact]
    public void A_generation_rate_that_does_not_price_output_stops_the_deployment()
    {
        // Generation produces output. A rate that cannot price it would leave
        // every such call unrated for a reason nobody would think to look for.
        AiModelRateOptions entry = Entry("model-a", "TextGeneration", 2m, output: null);

        Assert.Throws<InvalidOperationException>(() => Catalogue(entry));
    }

    [Fact]
    public void A_bad_currency_stops_the_deployment()
    {
        AiModelRateOptions entry = Entry("model-a", "TextGeneration", 2m, output: 8m);

        entry.Currency = "dollars";

        Assert.Throws<InvalidOperationException>(() => Catalogue(entry));
    }

    [Fact]
    public void An_embedding_rate_needs_no_output_price()
    {
        // Embeddings produce no output tokens, so requiring a price for them
        // would make every embedding model impossible to configure.
        AiProviderPricingCatalog catalogue = Catalogue(Entry("embed-a", "Embedding", 0.02m, output: null));

        AiModelRates? rates = catalogue.Find(
            AiProvider.OpenAi, ModelName.Create("embed-a"), AiOperationType.Embedding, Noon);

        Assert.NotNull(rates);
        Assert.Null(rates.OutputPerMillion);
    }

    [Fact]
    public void Currencies_are_carried_rather_than_assumed()
    {
        AiModelRateOptions entry = Entry("model-eu", "TextGeneration", 2m, output: 8m);

        entry.Currency = "eur";

        AiModelRates? rates = Catalogue(entry).Find(
            AiProvider.OpenAi, ModelName.Create("model-eu"), AiOperationType.TextGeneration, Noon);

        // Normalised, kept, and never turned into dollars: this platform has no
        // exchange rate and must not invent one.
        Assert.Equal("EUR", rates?.Currency.Value);
    }

    private static AiModelRates? Find(AiProviderPricingCatalog catalogue, DateTimeOffset at) =>
        catalogue.Find(
            AiProvider.OpenAi, ModelName.Create("model-a"), AiOperationType.TextGeneration, at);

    private static AiProviderPricingCatalog Catalogue(params AiModelRateOptions[] entries) =>
        AiProviderPricingCatalog.Create(entries);

    private static AiModelRateOptions Entry(
        string model,
        string operation,
        decimal input,
        decimal? output,
        DateTimeOffset? from = null) =>
        new()
        {
            Provider = nameof(AiProvider.OpenAi),
            Model = model,
            Operation = operation,
            Currency = "USD",
            EffectiveFrom = from ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            InputPerMillion = input,
            OutputPerMillion = output,
        };
}
