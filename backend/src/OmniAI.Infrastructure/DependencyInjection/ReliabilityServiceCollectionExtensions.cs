using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Knowledge;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.Knowledge;
using OmniAI.Infrastructure.Reliability;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>Stale-work reconciliation: provider operations, indexing attempts and conversation turns left in flight.</summary>
public static class ReliabilityServiceCollectionExtensions
{
    public static IServiceCollection AddOmniAiReliability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The same values the provider clients are built with, so the stale
        // thresholds are checked against what this deployment actually allows.
        OpenAiOptions openAi = new();
        configuration.GetSection(OpenAiOptions.SectionName).Bind(openAi);

        EmbeddingOptions embeddings = new();
        configuration.GetSection(EmbeddingOptions.SectionName).Bind(embeddings);

        KnowledgeChunkingPolicy chunking = new();

        TimeSpan providerTimeout = openAi.Timeout > embeddings.Timeout ? openAi.Timeout : embeddings.Timeout;
        int calls = (int)Math.Ceiling(chunking.MaxChunks / (double)Math.Max(1, embeddings.MaxBatchSize));

        ReliabilityOptions options = new();
        configuration.GetSection(ReliabilityOptions.SectionName).Bind(options);
        options.Validate(providerTimeout, embeddings.Timeout * calls);

        services.Configure<ReliabilityOptions>(configuration.GetSection(ReliabilityOptions.SectionName));

        services.AddScoped<ProviderUsageReconciler>();
        services.AddScoped<KnowledgeAttemptReconciler>();
        services.AddScoped<ConversationTurnReconciler>();
        services.AddHostedService<StaleWorkReconciliationWorker>();

        return services;
    }
}
