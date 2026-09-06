using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace OmniAI.Api.Security;

/// <summary>
/// Refuses to serve a non-development request that did not arrive over HTTPS.
/// </summary>
/// <remarks>
/// <strong>This is the fail-safe, and it exists because the alternative is a
/// silent downgrade.</strong> If a deployment's reverse proxy is misconfigured —
/// the forwarded header is missing, or the proxy is not on the trusted list — then
/// <c>Request.IsHttps</c> is <see langword="false"/> and this process cannot tell
/// whether the browser used TLS. There are only two honest responses to that, and
/// issuing cookies as though everything were fine is not one of them.
/// <para>
/// What this platform does <em>not</em> do is relax the <c>Secure</c> attribute to
/// make the request succeed. That would let the browser send the session cookie in
/// the clear, which is precisely the outcome the attribute prevents — solving a
/// deployment problem by removing a security guarantee.
/// </para>
/// <para>
/// So the request is refused, loudly. Nothing downstream runs: no cookie is
/// issued, no session is established, no antiforgery token is minted. An operator
/// gets a <c>Critical</c> log entry naming the configuration to check, and the
/// caller gets a response that says the server is misconfigured without saying
/// anything about its topology.
/// </para>
/// <para>
/// <strong>Development is exempt entirely</strong>, because local development over
/// <c>http://localhost</c> must keep working and there is no proxy to forward
/// anything. That exemption is keyed on <c>IsDevelopment()</c>, so Staging,
/// Production and any environment nobody has invented yet get the strict
/// behaviour — including a deployment that forgot to set
/// <c>ASPNETCORE_ENVIRONMENT</c> at all.
/// </para>
/// <para>
/// <strong>The liveness probe is exempt</strong>, and only it. An orchestrator
/// calls <c>/health</c> over the internal hop, before and independently of any
/// proxy; a probe that could not answer would report a healthy process as dead. It
/// discloses only that the process is running, sets no cookie and reads no
/// session.
/// </para>
/// </remarks>
internal sealed partial class SecureTransportMiddleware(
    RequestDelegate next,
    IHostEnvironment environment,
    ILogger<SecureTransportMiddleware> logger)
{
    /// <summary>The one path allowed over plain HTTP outside Development.</summary>
    private static readonly PathString HealthPath = new("/health");

    private readonly RequestDelegate _next =
        next ?? throw new ArgumentNullException(nameof(next));

    private readonly IHostEnvironment _environment =
        environment ?? throw new ArgumentNullException(nameof(environment));

    private readonly ILogger<SecureTransportMiddleware> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task InvokeAsync(HttpContext context, IProblemDetailsService problemDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problemDetails);

        if (_environment.IsDevelopment()
            || context.Request.IsHttps
            || context.Request.Path.StartsWithSegments(HealthPath))
        {
            await _next(context).ConfigureAwait(false);

            return;
        }

        // Critical, not Warning. Every authenticated request to this deployment is
        // failing, and the cause is a configuration mistake an operator has to fix
        // -- not something that will clear on its own.
        //
        // The remote address is recorded because it is exactly what the operator
        // needs to add to the trusted list, and it is not sensitive: it is the
        // peer that connected to this socket.
        LogInsecureTransport(
            _logger,
            _environment.EnvironmentName,
            context.Connection.RemoteIpAddress,
            ForwardedHeadersSetup.SectionName);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Insecure transport",
                // Deliberately says nothing about proxies, trusted addresses or
                // which header was missing. A caller cannot fix this and an
                // attacker should not be handed a description of the topology.
                Detail = "This deployment is not configured to serve requests securely.",
            },
        }).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Critical,
        Message = "Refusing a request in the {Environment} environment: it did not arrive over "
            + "HTTPS and no trusted proxy established the original scheme. The connection came "
            + "from {RemoteAddress}. Check that the reverse proxy sends X-Forwarded-Proto and "
            + "that its address is trusted through the {ConfigurationSection} configuration "
            + "section. No cookie was issued.")]
    private static partial void LogInsecureTransport(
        ILogger logger,
        string environment,
        IPAddress? remoteAddress,
        string configurationSection);
}

/// <summary>Pipeline registration for <see cref="SecureTransportMiddleware"/>.</summary>
internal static class SecureTransportMiddlewareExtensions
{
    /// <summary>
    /// Refuses non-HTTPS requests outside Development.
    /// </summary>
    /// <remarks>
    /// Must be registered <strong>after</strong> <c>UseForwardedHeaders</c>, which
    /// is what makes <c>Request.IsHttps</c> reflect the browser's scheme rather
    /// than the proxy hop's. Registered before everything else, so a misconfigured
    /// deployment refuses at the door rather than part-way through a request.
    /// </remarks>
    public static IApplicationBuilder UseOmniAiSecureTransport(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SecureTransportMiddleware>();
    }
}
