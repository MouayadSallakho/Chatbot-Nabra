using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Application.Tenants.Persistence;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Infrastructure.Persistence.Repositories;

namespace OmniAI.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Infrastructure implementations of Application-owned contracts.
/// </summary>
/// <remarks>
/// Registration lives beside the implementations, so the composition root does
/// not need to know that persistence is EF Core, that the provider is Npgsql, or
/// which concrete type satisfies <see cref="ITenantRepository"/>.
/// <para>
/// Written by hand rather than by assembly scanning. Two registrations do not
/// justify a convention-based scanner, and an explicit list is the one form of
/// this code that cannot silently register -- or silently miss -- a type.
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

        return services;
    }
}
