WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// OpenAPI describes the HTTP surface that the Angular admin application consumes.
builder.Services.AddOpenApi();

// Problem Details is the single, uniform error contract for the whole API.
builder.Services.AddProblemDetails();

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

// TLS is terminated by the reverse proxy in front of this process, so the
// application deliberately does not perform its own HTTPS redirection.

await app.RunAsync();

/// <summary>
/// Exposed so the API integration test project can host this application in memory.
/// </summary>
public partial class Program;
