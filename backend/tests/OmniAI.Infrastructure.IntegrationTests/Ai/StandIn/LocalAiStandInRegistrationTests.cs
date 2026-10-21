using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai;
using OmniAI.Application.Ai.Usage;
using OmniAI.Application.Knowledge;
using OmniAI.Domain.Ai;
using OmniAI.Infrastructure.Ai;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.Ai.StandIn;
using OmniAI.Infrastructure.Ai.Usage;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.Knowledge;

namespace OmniAI.Infrastructure.IntegrationTests.Ai.StandIn;

/// <summary>
/// When the local AI stand-in is composed, and when the process refuses to
/// start instead (Phase 37.2).
/// </summary>
/// <remarks>
/// <para>
/// The matrix of plan item 5: (Development, switch) composes the stand-in;
/// the switch outside Development, with another value, or with the live opt-in
/// stops startup; no switch is today's behaviour. Its zero rate exists only with
/// it.
/// </para>
/// <para>
/// Built from in-memory settings, exactly as the composition root passes them.
/// No process environment variable is set or read, no credential is real, and
/// nothing makes a request.
/// </para>
/// </remarks>
public sealed class LocalAiStandInRegistrationTests
{
    private const string PlaceholderKey = "test-key-not-a-real-credential";

    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static IConfiguration Settings(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

    private static ServiceProvider Build(bool development, IConfiguration configuration)
    {
        ServiceCollection services = [];

        services.AddLogging();
        services.AddOmniAiRuntime(
            configuration,
            requireLiveProviderOptIn: development,
            allowLocalStandIn: development);

        return services.BuildServiceProvider();
    }

    private static AiModelRates? StandInRate(ServiceProvider provider, AiOperationType operation) =>
        provider.GetRequiredService<IAiProviderPricing>().Find(
            AiProvider.DevelopmentStandIn,
            ModelName.Create(LocalAiStandInOptions.Model),
            operation,
            Now);

    // ------------------------------------------------------------ composed

    [Fact]
    public void Development_with_the_switch_composes_the_stand_in_for_both_capabilities()
    {
        using ServiceProvider provider = Build(
            development: true,
            Settings((LocalAiStandInSwitch.Variable, "1")));

        Assert.IsType<LocalStandInTextGenerator>(provider.GetRequiredService<IAiTextGenerator>());

        ITextEmbeddingGenerator embeddings = provider.GetRequiredService<ITextEmbeddingGenerator>();

        Assert.IsType<LocalStandInEmbeddingGenerator>(embeddings);
        Assert.Equal("dev:standin:1536", embeddings.Profile.Id.Value);
        Assert.Equal(EmbeddingOptions.Dimensions, embeddings.Profile.Dimensions);

        // What the knowledge screens read is the same profile, so readiness and
        // retrieval agree with the vectors that are written.
        Assert.Equal(embeddings.Profile, provider.GetRequiredService<EmbeddingProfile>());
    }

    [Fact]
    public void A_key_on_the_machine_does_not_stop_the_stand_in_or_reach_the_provider()
    {
        using ServiceProvider provider = Build(
            development: true,
            Settings(
                (LocalAiStandInSwitch.Variable, "1"),
                (OpenAiOptions.ApiKeyVariable, PlaceholderKey)));

        Assert.IsType<LocalStandInTextGenerator>(provider.GetRequiredService<IAiTextGenerator>());
        Assert.IsType<LocalStandInEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
    }

    [Fact]
    public void The_stand_in_comes_with_its_zero_rate_for_both_operations()
    {
        using ServiceProvider provider = Build(
            development: true,
            Settings((LocalAiStandInSwitch.Variable, "1")));

        AiModelRates generation = Assert.IsType<AiModelRates>(
            StandInRate(provider, AiOperationType.TextGeneration));
        AiModelRates embedding = Assert.IsType<AiModelRates>(
            StandInRate(provider, AiOperationType.Embedding));

        Assert.Equal(0m, generation.Rate(ProviderTokenUsage.Create(inputTokens: 120, outputTokens: 40)));
        Assert.Equal(0m, embedding.Rate(ProviderTokenUsage.Create(inputTokens: 900)));
    }

    [Fact]
    public void The_reply_delay_is_read_from_settings()
    {
        using ServiceProvider provider = Build(
            development: true,
            Settings(
                (LocalAiStandInSwitch.Variable, "1"),
                ("LocalAiStandIn:ReplyDelayMilliseconds", "250")));

        Assert.Equal(
            TimeSpan.FromMilliseconds(250),
            provider.GetRequiredService<LocalAiStandInOptions>().ReplyDelay);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("60001")]
    [InlineData("soon")]
    public void An_unusable_reply_delay_stops_startup(string delay)
    {
        Assert.Throws<InvalidOperationException>(() => Build(
            development: true,
            Settings(
                (LocalAiStandInSwitch.Variable, "1"),
                ("LocalAiStandIn:ReplyDelayMilliseconds", delay))));
    }

    // ------------------------------------------------------------- refused

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("true")]
    public void The_switch_outside_development_stops_startup(string value)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => Build(development: false, Settings((LocalAiStandInSwitch.Variable, value))));

        Assert.Contains(LocalAiStandInSwitch.Variable, refused.Message, StringComparison.Ordinal);
        Assert.Contains("not a Development environment", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("1 ")]
    [InlineData("01")]
    public void Any_value_but_one_stops_startup_in_development(string value)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => Build(development: true, Settings((LocalAiStandInSwitch.Variable, value))));

        Assert.Contains("must be exactly 1", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_switch_with_the_live_opt_in_stops_startup()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Build(
            development: true,
            Settings(
                (LocalAiStandInSwitch.Variable, "1"),
                (OpenAiOptions.LiveProviderOptInVariable, OpenAiOptions.LiveProviderOptInValue),
                (OpenAiOptions.ApiKeyVariable, PlaceholderKey))));

        Assert.Contains(OpenAiOptions.LiveProviderOptInVariable, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_switch_is_refused_by_the_check_the_composition_root_runs_first()
    {
        Assert.Throws<InvalidOperationException>(() => LocalAiStandInSwitch.IsEnabled(
            Settings((LocalAiStandInSwitch.Variable, "1")),
            development: false));
    }

    // ---------------------------------------------------- today's behaviour

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    [InlineData(false, null)]
    [InlineData(false, "")]
    public void Without_the_switch_nothing_changes_and_there_is_no_stand_in_rate(
        bool development,
        string? value)
    {
        using ServiceProvider provider = Build(
            development,
            Settings((LocalAiStandInSwitch.Variable, value)));

        Assert.IsType<UnconfiguredAiTextGenerator>(provider.GetRequiredService<IAiTextGenerator>());
        Assert.IsType<UnconfiguredTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
        Assert.Equal(
            new EmbeddingOptions().ProfileId,
            provider.GetRequiredService<EmbeddingProfile>().Id);

        Assert.Null(StandInRate(provider, AiOperationType.TextGeneration));
        Assert.Null(StandInRate(provider, AiOperationType.Embedding));
    }

    [Fact]
    public void Without_the_switch_the_live_opt_in_still_composes_the_real_adapters()
    {
        using ServiceProvider provider = Build(
            development: true,
            Settings(
                (OpenAiOptions.ApiKeyVariable, PlaceholderKey),
                (OpenAiOptions.LiveProviderOptInVariable, OpenAiOptions.LiveProviderOptInValue)));

        Assert.IsType<OpenAiTextGenerator>(provider.GetRequiredService<IAiTextGenerator>());
        Assert.IsType<OpenAiTextEmbeddingGenerator>(
            provider.GetRequiredService<ITextEmbeddingGenerator>());
        Assert.Null(StandInRate(provider, AiOperationType.TextGeneration));
    }

    // ---------------------------------------------------------------- rates

    [Fact]
    public void Configured_rates_are_unchanged_by_the_stand_in_rate()
    {
        AiModelRateOptions configured = new()
        {
            Model = "model-a",
            Operation = nameof(AiOperationType.TextGeneration),
            EffectiveFrom = Now.AddYears(-1),
            InputPerMillion = 2m,
            OutputPerMillion = 8m,
        };

        AiProviderPricingCatalog without = AiProviderPricingCatalog.Create([configured]);
        AiProviderPricingCatalog with = AiProviderPricingCatalog.Create([configured], includeLocalStandIn: true);

        AiModelRates? plain = without.Find(
            AiProvider.OpenAi, ModelName.Create("model-a"), AiOperationType.TextGeneration, Now);
        AiModelRates? alongside = with.Find(
            AiProvider.OpenAi, ModelName.Create("model-a"), AiOperationType.TextGeneration, Now);

        Assert.NotNull(plain);
        Assert.Equal(plain, alongside);
        Assert.Null(without.Find(
            AiProvider.DevelopmentStandIn,
            ModelName.Create(LocalAiStandInOptions.Model),
            AiOperationType.TextGeneration,
            Now));
    }

    [Fact]
    public void A_call_reporting_no_usage_stays_unrated_even_at_the_zero_rate()
    {
        AiModelRates? rate = AiProviderPricingCatalog.Create(null, includeLocalStandIn: true).Find(
            AiProvider.DevelopmentStandIn,
            ModelName.Create(LocalAiStandInOptions.Model),
            AiOperationType.TextGeneration,
            Now);

        Assert.NotNull(rate);
        Assert.Null(rate.Rate(ProviderTokenUsage.Unreported));
    }

    [Theory]
    [InlineData("DevelopmentStandIn")]
    [InlineData("developmentstandin")]
    [InlineData("2")]
    public void A_configured_rate_for_the_stand_in_is_refused(string provider)
    {
        AiModelRateOptions configured = new()
        {
            Provider = provider,
            Model = LocalAiStandInOptions.Model,
            Operation = nameof(AiOperationType.TextGeneration),
            EffectiveFrom = Now.AddYears(-1),
            InputPerMillion = 0m,
            OutputPerMillion = 0m,
        };

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => AiProviderPricingCatalog.Create([configured]));

        Assert.Contains("built in", refused.Message, StringComparison.Ordinal);
    }
}
