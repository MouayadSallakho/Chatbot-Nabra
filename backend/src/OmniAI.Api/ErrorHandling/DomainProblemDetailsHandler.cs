using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OmniAI.Application.Ai;
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

        LogOutcome(logger, status, detail);

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
    /// Records an expected outcome at the level its status deserves.
    /// </summary>
    /// <remarks>
    /// A caller's mistake and a provider outage are both handled here, and they
    /// are emphatically not the same event to an operator, so the status decides
    /// the level rather than one level covering both.
    /// </remarks>
    private static void LogOutcome(ILogger logger, int statusCode, string reason)
    {
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            LogUpstreamFailure(logger, statusCode, reason);
        }
        else
        {
            LogRejected(logger, statusCode, reason);
        }
    }

    /// <summary>
    /// Records an expected rejection.
    /// </summary>
    /// <remarks>
    /// Information, not Error: a rejected name, a taken slug, a message that
    /// breaks a platform limit, a draft that cannot be disabled and a write that
    /// lost a race are all the API working correctly. Logging them at error level
    /// would fill an alert channel with other people's typing mistakes and lost
    /// races and bury real faults. The message text is the exception's own
    /// explanation of a business rule and carries no connection string,
    /// credential or database detail.
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
    /// Records a capability this deployment could not deliver.
    /// </summary>
    /// <remarks>
    /// Warning, because an unconfigured AI runtime and an upstream that answers
    /// with nothing usable are both things an operator has to act on, and neither
    /// is a caller's fault -- logging them at the same level as somebody's typing
    /// mistake would mean a provider outage left no more trace than a rejected
    /// name. Not Error, because both are states this code recognises and reports
    /// deliberately rather than faults it failed to anticipate; the unhandled path
    /// still logs at error level.
    /// <para>
    /// The reason recorded is the exception's own fixed sentence. It carries no
    /// preview message, no agent instructions, no provider name and no upstream
    /// response, so turning this level up cannot start writing customers' text or
    /// a tenant's configuration into the log.
    /// </para>
    /// </remarks>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Request could not be served with {StatusCode}: {Reason}")]
    private static partial void LogUpstreamFailure(ILogger logger, int statusCode, string reason);

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

        // Text a caller offered as a message that this platform will not process:
        // blank, past the length bound, or carrying control characters. 400 for
        // the same reason a rejected name is -- the caller can see what it sent,
        // change it and try again.
        //
        // Not a Domain failure, and it is worth being precise about why: no
        // aggregate was consulted. This is a limit the platform places on a
        // request it will accept, so the Application raises its own type and the
        // Domain is never asked a question about a message it does not model.
        AiTurnValidationException message => (
            StatusCodes.Status400BadRequest,
            "Invalid request details",
            message.Message),

        // No AI provider is configured on this deployment. 503, because the
        // request is perfectly good and the capability is simply not there --
        // the same request will succeed once a provider is configured, without
        // the caller changing anything.
        //
        // Not 500: nothing failed unexpectedly, and calling a known,
        // deliberate configuration state a server fault would send an operator
        // hunting for a bug in code that is behaving exactly as intended. Not
        // 501 either, which would claim the platform does not implement preview
        // at all rather than that this deployment cannot currently serve it.
        //
        // Deliberately no Retry-After. The platform has no idea when a provider
        // will be configured -- that is a deployment decision, not a queue that
        // drains -- and inventing a number would invite clients to poll on a
        // schedule with nothing behind it.
        AiRuntimeUnavailableException unavailable => (
            StatusCodes.Status503ServiceUnavailable,
            "AI generation unavailable",
            unavailable.Message),

        // The provider did not answer inside this deployment's own patience. 504,
        // which is precisely what it means: this server, acting as a gateway, did
        // not get a timely response from upstream.
        //
        // Separated from 503 because the two tell a caller different things. 503
        // says the platform could not get the work started -- nothing was spent,
        // and the same request will work once the deployment is fixed. This says
        // the work probably did start and the answer did not arrive, so a shorter
        // question might succeed where a repeat of the same one would not.
        //
        // Never produced by caller cancellation: a closed tab is an
        // OperationCanceledException, which this handler does not map at all.
        AiRuntimeTimeoutException timeout => (
            StatusCodes.Status504GatewayTimeout,
            "AI generation timed out",
            timeout.Message),

        // Something answered, and what came back was not a usable reply. 502,
        // which is exactly what it describes: this server acted as a gateway to
        // an upstream service and received an invalid response from it. The
        // caller did nothing wrong and cannot fix it, so a 4xx would misdirect
        // them entirely.
        //
        // The detail is the exception's fixed sentence, and the exception carries
        // no provider name, status line, response body or request id -- so an
        // upstream failure cannot become a way to learn which vendor this
        // platform uses or how it is failing.
        AiResponseInvalidException invalid => (
            StatusCodes.Status502BadGateway,
            "AI generation failed",
            invalid.Message),

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
