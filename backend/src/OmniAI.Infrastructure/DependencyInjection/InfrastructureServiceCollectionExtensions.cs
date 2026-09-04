using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Ai;
using OmniAI.Application.AiAgents.Persistence;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Infrastructure.Ai;
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
        services.AddScoped<IAiAgentRepository, AiAgentRepository>();

        return services;
    }

    /// <summary>
    /// Adds the AI runtime.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// <para>
    /// Separate from persistence, because it is a separate capability with a
    /// separate reason to change: a deployment can have a database and no AI
    /// provider, which is exactly the state this registration describes.
    /// </para>
    /// <para>
    /// <strong>What it registers today is the refusal.</strong> No provider is
    /// configured, so <see cref="IAiTextGenerator"/> resolves to
    /// <see cref="UnconfiguredAiTextGenerator"/> and every generation attempt
    /// fails explicitly. Registering it -- rather than leaving the interface
    /// unregistered -- is what turns "AI is not set up on this deployment" into a
    /// clear answer instead of a dependency-injection failure that reads like a
    /// bug in the endpoint.
    /// </para>
    /// <para>
    /// The phase that adds a real provider replaces this one line, and chooses the
    /// implementation from configuration. Nothing above this method changes when
    /// that happens: the use case, the endpoint and the contract are already
    /// provider-independent.
    /// </para>
    /// <para>
    /// Singleton because the current implementation is stateless and holds
    /// nothing per request. A real adapter will own a pooled HTTP client and wants
    /// the same lifetime for the same reason.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOmniAiRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAiTextGenerator, UnconfiguredAiTextGenerator>();

        return services;
    }
}
