using OmniAI.Api.AiAgents;
using OmniAI.Api.Composition;
using OmniAI.Api.Tenants;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// OpenAPI describes the HTTP surface that the Angular admin application consumes.
builder.Services.AddOpenApi();

// Problem Details is the single, uniform error contract for the whole API.
builder.Services.AddProblemDetails();

// Persistence, use cases and error translation. See CompositionRoot.
builder.Services.AddOmniAi(builder.Configuration);

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness probe. Intentionally dependency-free: it answers "is the process up",
// not "are downstream services healthy". Readiness checks arrive with the
// components they check.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("GetHealth");

// Tenant onboarding.
//
// NOT AUTHORISED FOR PRODUCTION EXPOSURE. These endpoints are unauthenticated
// because authentication and authorisation are not implemented yet, so anyone
// able to reach this process can create a tenant. Deployment is not authorised
// until that phase is complete. No placeholder credential stands in for the
// missing controls, deliberately: a fake one would hide the gap rather than close
// it.
app.MapTenantEndpoints();

// AI agent configuration, scoped to the tenant that owns each agent.
//
// NOT AUTHORISED FOR PRODUCTION EXPOSURE, for the same reason and with one
// addition. The tenant id in the route is supplied by the caller and believed,
// because there is no authenticated principal to check it against. Tenant
// scoping is enforced all the way into the SQL, so no request can read or
// change an agent through another tenant's route -- but until authentication
// exists, nothing stops a caller from naming whichever tenant it likes.
app.MapAiAgentEndpoints();

// TLS is terminated by the reverse proxy in front of this process, so the
// application deliberately does not perform its own HTTPS redirection.

await app.RunAsync();

/// <summary>
/// Exposed so the API integration test project can host this application in memory.
/// </summary>
public partial class Program;
