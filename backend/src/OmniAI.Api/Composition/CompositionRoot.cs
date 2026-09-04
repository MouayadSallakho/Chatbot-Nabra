using OmniAI.Api.ErrorHandling;
using OmniAI.Application.AiAgents.ActivateAiAgent;
using OmniAI.Application.AiAgents.CreateAiAgent;
using OmniAI.Application.AiAgents.DisableAiAgent;
using OmniAI.Application.AiAgents.GetAiAgentById;
using OmniAI.Application.AiAgents.RenameAiAgent;
using OmniAI.Application.AiAgents.UpdateAiAgentInstructions;
using OmniAI.Application.Tenants.CreateTenant;
using OmniAI.Application.Tenants.GetTenantById;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.Time;

namespace OmniAI.Api.Composition;

/// <summary>
/// Wires the application together.
/// </summary>
/// <remarks>
/// Exists so <c>Program.cs</c> stays a readable list of pipeline steps rather
/// than growing a new registration line for every use case the platform gains.
/// <para>
/// Every registration is explicit. No assembly scanning, no reflection-based
/// handler discovery and no mediator: a scanner would replace a readable list
/// with a convention that has to be learned, and would happily register or miss
/// a type without anyone noticing. Eight handlers is still a list worth reading.
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

        // The clock. TimeProvider is the BCL abstraction, so no third-party clock
        // package is involved and nothing in the Domain or Application had to invent
        // one. Tests substitute a deterministic instance, which is why no handler
        // calls DateTimeOffset.UtcNow itself. Singleton because it holds no
        // per-request state.
        //
        // Wrapped rather than registered raw: DateTimeOffset counts 100ns ticks and
        // PostgreSQL timestamptz stores microseconds, so an unwrapped system clock
        // hands out instants three digits finer than anything that can be written.
        // A create response would then report a createdAt that differs from the one
        // the very next read returns. See MicrosecondTimeProvider.
        services.AddSingleton<TimeProvider>(new MicrosecondTimeProvider(TimeProvider.System));

        // Application use cases. Registered as their concrete types, because
        // nothing substitutes them at runtime -- an interface per handler would be
        // ceremony with one implementation each. Scoped, so a handler shares the
        // request's DbContext and therefore its unit of work.
        services.AddScoped<CreateTenantHandler>();
        services.AddScoped<GetTenantByIdHandler>();
        services.AddScoped<CreateAiAgentHandler>();
        services.AddScoped<GetAiAgentByIdHandler>();
        services.AddScoped<RenameAiAgentHandler>();
        services.AddScoped<UpdateAiAgentInstructionsHandler>();
        services.AddScoped<ActivateAiAgentHandler>();
        services.AddScoped<DisableAiAgentHandler>();

        // Turns expected Domain and Application failures into Problem Details.
        // Registered before any future handler so its specific cases are
        // considered first.
        services.AddExceptionHandler<DomainProblemDetailsHandler>();

        return services;
    }
}
