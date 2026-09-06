using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Infrastructure.Ai;
using OmniAI.Infrastructure.Ai.OpenAi;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Infrastructure implementations of Application-owned contracts.
/// </summary>
/// <remarks>
/// Registration lives beside the implementations, so the composition root does
/// not need to know that persistence is EF Core, that the provider is Npgsql, or
/// which concrete types satisfy <see cref="ITenantRepository"/>,
/// <see cref="IAiAgentRepository"/> and <see cref="IAiTextGenerator"/>. That last
/// one is the point of the whole arrangement: the API can offer an AI capability
/// without any layer above this file knowing whether a provider exists, let alone
/// which.
/// <para>
/// Written by hand rather than by assembly scanning. A short explicit list
/// does not justify a convention-based scanner, and it is the one form of this
/// code that cannot silently register -- or silently miss -- a type.
/// </para>
/// </remarks>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds the PostgreSQL persistence layer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// The database connection string. It is a parameter rather than something
    /// read from <c>IConfiguration</c> here, because deciding where configuration
    /// comes from is the composition root's job -- and it keeps this project free
    /// of a configuration dependency it does not otherwise need.
    /// <para>
    /// May be blank. The provider is then configured without a connection string,
    /// so the application still starts and any endpoint that needs no database
    /// still works, while the first actual database call fails with EF Core's own
    /// explicit "connection string has not been initialized" error. That is
    /// deliberate: a misconfigured deployment should fail where it is used, with a
    /// message that says what is missing, rather than be papered over with a
    /// fabricated default that quietly points somewhere real.
    /// </para>
    /// </param>
    public static IServiceCollection AddOmniAiPersistence(
        this IServiceCollection services,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<OmniAiDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseNpgsql();
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        });

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantMembershipRepository, TenantMembershipRepository>();
        services.AddScoped<IAiAgentRepository, AiAgentRepository>();

        return services;
    }

    /// <summary>
    /// Adds the AI runtime: OpenAI when a key is configured, an explicit refusal
    /// when it is not.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Configuration carrying the non-secret <c>OpenAI</c> section. The API key is
    /// deliberately not read from here.
    /// </param>
    /// <remarks>
    /// <para>
    /// Separate from persistence, because it is a separate capability with a
    /// separate reason to change: a deployment can have a database and no AI
    /// provider, and that is a supported state rather than a broken one.
    /// </para>
    /// <para>
    /// <strong>The key's presence is what decides.</strong> With
    /// <c>OPENAI_API_KEY</c> set, <see cref="IAiTextGenerator"/> resolves to the
    /// OpenAI adapter. Without it, it resolves to
    /// <see cref="UnconfiguredAiTextGenerator"/> and every generation attempt
    /// fails explicitly — the accepted behaviour, unchanged. Either way the
    /// application starts, health checks answer and the whole agent management
    /// slice works: a developer who has not configured OpenAI must not be met
    /// with an application that refuses to run.
    /// </para>
    /// <para>
    /// <strong>The key is read here and nowhere else.</strong> It is read as
    /// <c>OPENAI_API_KEY</c> through <see cref="IConfiguration"/>, which in the
    /// hosted application is supplied by the environment-variable provider — so
    /// in production this is the environment variable and nothing else. Going
    /// through configuration rather than <c>Environment.GetEnvironmentVariable</c>
    /// is what makes this method testable: a test can prove both branches without
    /// mutating the process's environment, which is global state that would race
    /// across parallel test classes.
    /// </para>
    /// <para>
    /// It is handed straight to the client factory: not bound into
    /// <see cref="OpenAiOptions"/>, not stored in a field of ours, not logged,
    /// not included in any exception. Only its presence is ever tested. It must
    /// never be placed in <c>appsettings</c>, and a test asserts that no settings
    /// file in the repository contains it.
    /// </para>
    /// <para>
    /// Both implementations are singletons. The OpenAI client is documented as
    /// reusable and thread-safe, and it owns an HTTP stack: building one per
    /// request would exhaust sockets under any real load.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOmniAiRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string? apiKey = configuration[OpenAiOptions.ApiKeyVariable];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No provider. The honest refusal, exactly as before.
            services.AddSingleton<IAiTextGenerator, UnconfiguredAiTextGenerator>();

            return services;
        }

        // Read explicitly rather than through the configuration binder, which
        // would mean adding a package for three settings. Being explicit also
        // means an unrecognised key is visibly not read, instead of silently
        // ignored by a reflective bind.
        IConfigurationSection section = configuration.GetSection(OpenAiOptions.SectionName);

        OpenAiOptions options = new()
        {
            Model = Setting(section, nameof(OpenAiOptions.Model)) ?? OpenAiOptions.DefaultModel,
            MaxOutputTokens = Number(
                section,
                nameof(OpenAiOptions.MaxOutputTokens),
                OpenAiOptions.DefaultMaxOutputTokens),
            TimeoutSeconds = Number(
                section,
                nameof(OpenAiOptions.TimeoutSeconds),
                OpenAiOptions.DefaultTimeoutSeconds),
        };

        // Checked now rather than on the first request, so a mistyped setting is a
        // startup failure an operator sees rather than a runtime failure a
        // customer discovers.
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(OpenAiClientFactory.Create(apiKey, options));
        services.AddSingleton<IAiTextGenerator, OpenAiTextGenerator>();

        return services;
    }

    /// <summary>A configured string, or null when it is absent or blank.</summary>
    private static string? Setting(IConfigurationSection section, string key)
    {
        string? value = section[key];

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// A configured whole number, or the supplied default when it is absent.
    /// </summary>
    /// <remarks>
    /// A present but unparseable value is an error rather than a silent fallback.
    /// Quietly substituting a default would mean a deployment that meant to cap
    /// spending at 200 tokens, and mistyped it, ran at whatever this code happens
    /// to prefer without anyone being told.
    /// </remarks>
    private static int Number(IConfigurationSection section, string key, int fallback)
    {
        string? value = Setting(section, key);

        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"{OpenAiOptions.SectionName}:{key} must be a whole number.");
    }
}
