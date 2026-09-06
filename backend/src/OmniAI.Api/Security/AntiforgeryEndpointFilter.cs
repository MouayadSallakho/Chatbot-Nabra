using Microsoft.AspNetCore.Antiforgery;

namespace OmniAI.Api.Security;

/// <summary>
/// Rejects an unsafe request that does not carry a valid antiforgery token.
/// </summary>
/// <remarks>
/// <strong>Written explicitly rather than left to <c>UseAntiforgery</c>.</strong>
/// The middleware validates only endpoints the framework has decided require it,
/// which for minimal APIs means those binding form data. Every endpoint here
/// takes JSON, so none of them would have been validated and the protection would
/// have looked present while doing nothing. A filter applied to a route group is
/// visible at the mapping site, so a new group without it is a missing line
/// somebody can see rather than an absent metadata attribute nobody can.
/// <para>
/// <strong>SameSite is not the whole design.</strong> The session cookie is
/// <c>SameSite=Lax</c>, which already withholds it from cross-site POSTs in
/// current browsers -- but that is one control, implemented by the browser,
/// varying by version, and bypassed by anything the browser treats as same-site.
/// The token is the control this server enforces itself and does not depend on
/// what the client happens to implement.
/// </para>
/// <para>
/// <strong>Read-only requests are exempt, by method.</strong> GET, HEAD, OPTIONS
/// and TRACE change nothing, and requiring a token on them would mean the browser
/// could not follow a link into the application. That exemption is why the
/// preview endpoint is <em>not</em> exempt despite storing nothing: it is a POST
/// that costs money at a third party, and a cross-site request that spends this
/// deployment's provider budget is a real attack even though no row changes.
/// </para>
/// </remarks>
internal sealed partial class AntiforgeryEndpointFilter(
    IAntiforgery antiforgery,
    ILogger<AntiforgeryEndpointFilter> logger) : IEndpointFilter
{
    private readonly IAntiforgery _antiforgery =
        antiforgery ?? throw new ArgumentNullException(nameof(antiforgery));

    private readonly ILogger<AntiforgeryEndpointFilter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;

        if (!ChangesState(http.Request.Method))
        {
            return await next(context).ConfigureAwait(false);
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException failure)
        {
            // Information, not Error. A missing token is a client that has not
            // bootstrapped yet or a session that outlived its token, and both are
            // ordinary; logging them as faults would bury real ones. The
            // exception's message is recorded for an operator and never returned:
            // it distinguishes a missing cookie from a mismatched token, which is
            // useful in a log and is a hint worth withholding from an attacker.
            LogRejected(_logger, failure.Message);

            return Results.Problem(
                title: "Antiforgery validation failed",
                detail: "This request did not carry a valid antiforgery token. "
                    + "Reload the page and try again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether this method may change state and therefore needs a token.
    /// </summary>
    /// <remarks>
    /// An allow-list of safe methods rather than a deny-list of unsafe ones.
    /// A method nobody anticipated is then protected by default instead of
    /// slipping through, which is the direction a security check should fail in.
    /// </remarks>
    private static bool ChangesState(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method)
        && !HttpMethods.IsTrace(method);

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Request rejected: antiforgery validation failed ({Reason})")]
    private static partial void LogRejected(ILogger logger, string reason);
}
