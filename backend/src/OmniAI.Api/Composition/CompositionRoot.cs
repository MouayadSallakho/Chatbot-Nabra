using OmniAI.Api.ErrorHandling;
using OmniAI.Application.Tenants.CreateTenant;
using OmniAI.Application.Tenants.GetTenantById;
using OmniAI.Infrastructure.DependencyInjection;

namespace OmniAI.Api.Composition;

/// <summary>
/// Wires the application together.
/// </summary>
/// <remarks>
/// Exists so <c>Program.cs</c> stays a readable list of pipeline steps rather
/// than growing a new registration line for every use case the platform gains.
/// <para>
/// Every registration is explicit. No assembly scanning, no reflection-based
/// handler discovery and no mediator: with two use cases, a scanner would replace
/// two readable lines with a convention that has to be learned, and would happily
/// register or miss a type without anyone noticing.
/// </para>
/// </remarks>
internal static class CompositionRoot
{
    /// <summary>
    /// Configuration key holding the platform database connection string.
    /// </summary>
    /// <remarks>
    /// Supplied by the environment -- an environment variable, user secrets, or a
    /// deployment secret store. It is deliberately blank in
    /// <c>appsettings.json</c>: a real connection string in source control is a
    /// committed credential, and a plausible-looking default is worse, because a
    /// misconfigured deployment would silently connect somewhere.
    /// </remarks>
    private const string DatabaseConnectionName = "OmniAiDatabase";

    public static IServiceCollection AddOmniAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Infrastructure decides how persistence is built; the composition root
        // only decides where its configuration comes from.
        services.AddOmniAiPersistence(configuration.GetConnectionString(DatabaseConnectionName));

        // Application use cases. Registered as their concrete types, because
        // nothing substitutes them at runtime -- an interface per handler would be
        // ceremony with one implementation each. Scoped, so a handler shares the
        // request's DbContext and therefore its unit of work.
        services.AddScoped<CreateTenantHandler>();
        services.AddScoped<GetTenantByIdHandler>();

        // Turns expected Domain and Application failures into Problem Details.
        // Registered before any future handler so its specific cases are
        // considered first.
        services.AddExceptionHandler<TenantProblemDetailsHandler>();

        return services;
    }
}
