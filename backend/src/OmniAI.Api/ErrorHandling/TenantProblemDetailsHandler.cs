using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OmniAI.Application.Tenants;
using OmniAI.Domain;

namespace OmniAI.Api.ErrorHandling;

/// <summary>
/// Translates the expected Domain and Application failures into Problem Details.
/// </summary>
/// <remarks>
/// The single place where a business outcome becomes a status code. The Domain
/// knows nothing about HTTP and the Application knows nothing about transport, so
/// something at the edge has to make that decision -- and doing it here means it
/// is made once, not repeated in every endpoint.
/// <para>
/// It plugs into the pipeline Phase 1 established: <c>AddProblemDetails</c> plus
/// <c>UseExceptionHandler</c>. Anything this handler does not recognise is left
/// alone by returning <see langword="false"/>, so an unexpected exception keeps
/// its existing behaviour -- a <c>500</c> whose body carries no message, no type
/// name and no stack trace.
/// </para>
/// </remarks>
internal sealed partial class TenantProblemDetailsHandler(
    IProblemDetailsService problemDetails,
    ILogger<TenantProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        (int status, string title, string detail)? mapped = Map(exception);

        if (mapped is null)
        {
            // Not an expected outcome. Leave it to the default handler, which
            // does not disclose internals to the client.
            return false;
        }

        (int status, string title, string detail) = mapped.Value;

        LogRejected(logger, status, detail);

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
            },
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Records an expected rejection.
    /// </summary>
    /// <remarks>
    /// Information, not Error: a rejected name and a taken slug are the API
    /// working correctly. Logging them at error level would fill an alert channel
    /// with other people's typing mistakes and bury real faults. The message text
    /// is the exception's own explanation of a business rule and carries no
    /// connection string, credential or database detail.
    /// <para>
    /// Source-generated rather than a <c>LogInformation</c> call, so the message
    /// template is compiled, nothing is boxed into a <c>params</c> array, and the
    /// arguments are not assembled when the level is disabled.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Tenant request rejected with {StatusCode}: {Reason}")]
    private static partial void LogRejected(ILogger logger, int statusCode, string reason);

    /// <summary>
    /// The status code for an exception, or <see langword="null"/> if it is not
    /// an expected business outcome.
    /// </summary>
    /// <remarks>
    /// <c>Detail</c> is the exception's own message in both cases, and that is a
    /// deliberate, bounded decision rather than an oversight: these two types are
    /// raised only by the Domain value objects and by the tenant use case, their
    /// messages describe a business rule and echo caller-supplied input, and
    /// neither can carry a database identifier, SQL text or a credential. No
    /// other exception's message reaches a client.
    /// </remarks>
    private static (int Status, string Title, string Detail)? Map(Exception exception) => exception switch
    {
        // A value the business rejects: the caller can fix it and retry.
        DomainValidationException validation => (
            StatusCodes.Status400BadRequest,
            "Invalid tenant details",
            validation.Message),

        // A well-formed request that lost a race, or arrived late, for a slug
        // somebody else already holds.
        TenantSlugAlreadyExistsException conflict => (
            StatusCodes.Status409Conflict,
            "Tenant slug already exists",
            conflict.Message),

        // A request the transport layer could not even read -- a body that is not
        // valid JSON of the expected shape, for instance. This is not a Domain
        // failure and never reaches a use case, but it still has to answer with
        // the status it already carries rather than being mistaken for a server
        // fault: the pipeline's default handler does not read that status, so
        // without this arm a malformed body returns 500. The detail is a fixed
        // sentence rather than the framework's parser message, which can quote
        // the offending payload back to the caller.
        BadHttpRequestException malformed => (
            malformed.StatusCode,
            "Malformed request",
            "The request body could not be read. It must be JSON matching the documented shape."),

        _ => null,
    };
}
