using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OmniAI.Application.AiAgents;
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
/// <para>
/// Named for the Domain rather than for one aggregate, because it never was
/// tenant-specific: <c>DomainValidationException</c> is raised by every value
/// object in the model, and now serves AI agents as well as tenants. Leaving the
/// old name in place would have meant either a class called
/// <c>TenantProblemDetailsHandler</c> deciding how AI agent failures are
/// reported, or a second handler duplicating the shared arms -- and the second
/// handler could not have worked, because nothing in a
/// <c>DomainValidationException</c> says which aggregate raised it.
/// </para>
/// </remarks>
internal sealed partial class DomainProblemDetailsHandler(
    IProblemDetailsService problemDetails,
    ILogger<DomainProblemDetailsHandler> logger) : IExceptionHandler
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
    /// Information, not Error: a rejected name, a taken slug, a draft that cannot
    /// be disabled and a write that lost a race are all the API working
    /// correctly. Logging them at error level would fill an alert channel with
    /// other people's typing mistakes and lost races and bury real faults. The
    /// message text is the exception's own explanation of a business rule and
    /// carries no connection string, credential or database detail.
    /// <para>
    /// Source-generated rather than a <c>LogInformation</c> call, so the message
    /// template is compiled, nothing is boxed into a <c>params</c> array, and the
    /// arguments are not assembled when the level is disabled.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Request rejected with {StatusCode}: {Reason}")]
    private static partial void LogRejected(ILogger logger, int statusCode, string reason);

    /// <summary>
    /// The status code for an exception, or <see langword="null"/> if it is not
    /// an expected business outcome.
    /// </summary>
    /// <remarks>
    /// <c>Detail</c> is the exception's own message in every mapped case, and that
    /// is a deliberate, bounded decision rather than an oversight. Each of these
    /// types is raised only by the Domain value objects, the aggregates, or the
    /// use cases; their messages describe a business rule and echo
    /// caller-supplied input, and none can carry SQL text, a constraint name, a
    /// SQLSTATE, a connection string or a credential -- Infrastructure translates
    /// provider failures into these types precisely so that nothing else reaches
    /// this method. No other exception's message reaches a client.
    /// </remarks>
    private static (int Status, string Title, string Detail)? Map(Exception exception) => exception switch
    {
        // A value the business rejects: the caller can fix it and retry. Raised by
        // every value object in the Domain, so it is not specific to one
        // aggregate and its title must not claim to be.
        DomainValidationException validation => (
            StatusCodes.Status400BadRequest,
            "Invalid request details",
            validation.Message),

        // A well-formed request the aggregate refuses in its current state --
        // disabling an agent that is still a draft. 409 rather than 400, because
        // nothing about the request is malformed: the same request would succeed
        // once the agent has been activated, so the conflict is with the state of
        // the resource rather than with what the caller wrote. 422 was considered
        // and rejected for the same reason -- the content is perfectly
        // processable, it just contradicts the resource as it stands.
        DomainStateException state => (
            StatusCodes.Status409Conflict,
            "Operation not allowed in the current state",
            state.Message),

        // A route naming a tenant that does not exist.
        TenantNotFoundException missingTenant => (
            StatusCodes.Status404NotFound,
            "Tenant not found",
            missingTenant.Message),

        // A well-formed request that lost a race, or arrived late, for a slug
        // somebody else already holds. Still 409: the tenant slug is not a
        // conditional-request resource and no precondition was involved.
        TenantSlugAlreadyExistsException conflict => (
            StatusCodes.Status409Conflict,
            "Tenant slug already exists",
            conflict.Message),

        // The agent is not at the version the caller said it was changing --
        // either because it was superseded long before the request arrived, or
        // because a transaction committed while the request was running.
        //
        // 412, not 409, and the distinction is the point. 409 says the operation
        // contradicts the resource and would still be refused after a re-read.
        // This says the caller's If-Match precondition was not satisfied: the
        // operation may be perfectly fine, the caller simply has not seen what the
        // agent looks like now. Only one of those is fixed by reading again, and
        // conflating them would tell a client to give up when it should retry --
        // or to retry when it should not.
        //
        // Nothing is retried automatically; see AiAgentVersionMismatchException
        // for why replaying the write would destroy the other writer's change.
        AiAgentVersionMismatchException stale => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition failed",
            stale.Message),

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
