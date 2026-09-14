using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OmniAI.Application.Ai;
using OmniAI.Application.AiAgents.ListAiAgents;
using OmniAI.Application.AiAgents;
using OmniAI.Application.Authorization;
using OmniAI.Application.Conversations.Realtime;
using OmniAI.Application.Conversations;
using OmniAI.Application.Invitations;
using OmniAI.Application.Knowledge;
using OmniAI.Application.Knowledge.ListKnowledgeDocuments;
using OmniAI.Application.Members;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Application.Security;
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

        // What the operator is told, which is not always what the caller is told.
        // A protection-store failure sends a visitor a deliberately vague
        // sentence; the log gets the setting or dependency that is actually at
        // fault, because that is the only place it is useful and the only place it
        // is safe.
        LogOutcome(logger, status, OperatorReason(exception, detail));

        httpContext.Response.StatusCode = status;

        // A refusal that says "too many" without saying "for how long" invites the
        // client to retry immediately, which is the behaviour the refusal exists
        // to stop. The value is the remaining life of whichever counter refused,
        // floored at a second so it can never read as "retry now".
        if (exception is PublicChatRateLimitedException limited)
        {
            httpContext.Response.Headers.RetryAfter = Math
                .Max(1, (int)Math.Ceiling(limited.RetryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

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
    /// What to write in the log, which may be more than the caller is told.
    /// </summary>
    /// <remarks>
    /// Every mapped exception's message is safe to log, and for almost all of them
    /// it is also the most useful thing to log. The exception is a public-chat
    /// protection failure: its message is written for a stranger and says nothing,
    /// so logging it would leave an operator with an outage and no cause. Its
    /// <c>Reason</c> names the missing setting or the unreachable dependency and
    /// carries no address, credential or customer text.
    /// </remarks>
    private static string OperatorReason(Exception exception, string detail) =>
        exception is PublicChatProtectionUnavailableException unprotected
            ? unprotected.Reason
            : detail;

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
        KnowledgeDocumentVersionMismatchException staleKnowledge => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition failed",
            staleKnowledge.Message),
        KnowledgeDocumentPageValidationException knowledgePaging => (
            StatusCodes.Status400BadRequest,
            "Invalid page request",
            knowledgePaging.Message),
        // The embedding family maps to the same three statuses as text
        // generation, and for the same reasons: unavailable is the deployment's
        // problem, a timeout is the provider's, and an unusable answer is a bad
        // gateway. They are separate exception types so an operator reading a log
        // can tell which capability failed.
        EmbeddingUnavailableException embeddingUnavailable => (
            StatusCodes.Status503ServiceUnavailable,
            "Knowledge indexing unavailable",
            embeddingUnavailable.Message),
        EmbeddingTimeoutException embeddingTimeout => (
            StatusCodes.Status504GatewayTimeout,
            "Knowledge indexing timed out",
            embeddingTimeout.Message),
        EmbeddingResponseInvalidException embeddingInvalid => (
            StatusCodes.Status502BadGateway,
            "Knowledge indexing failed",
            embeddingInvalid.Message),
        AiAgentVersionMismatchException stale => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition failed",
            stale.Message),

        // A member of a workspace attempted something their roles do not permit.
        // 403, and it is the one refusal in this platform that admits the
        // resource exists -- which is safe precisely because the caller has
        // already proved they belong to the workspace, so it tells them nothing
        // they did not know. It buys a person who can act: they can see they need
        // a permission and ask for it, instead of staring at a resource that
        // appears to have vanished.
        //
        // Never 404 for this case, and never 403 for a non-member. Getting that
        // pair the wrong way round would either confirm somebody else's workspace
        // is real or tell a member their own had disappeared.
        //
        // The detail names the permission, which is already public -- it is in
        // the catalogue every role editor reads -- and nothing else: no role name,
        // no member list, no tenant name and no hint about which record was
        // addressed.
        PermissionDeniedException denied => (
            StatusCodes.Status403Forbidden,
            "Permission denied",
            denied.Message),

        // The role is not at the version the caller said it was changing. 412,
        // for exactly the reasons recorded for the AI agent case: the operation
        // may be perfectly fine and the caller simply has not seen the role as it
        // stands, which is the one failure re-reading fixes.
        //
        // Deliberately distinct from the two conflicts a role can also produce. A
        // name collision means choose another name; a protected-role refusal means
        // that operation will never be allowed on that role. Reporting either as
        // stale would tell somebody to refresh a screen that refreshing cannot
        // help.
        TenantRoleVersionMismatchException staleRole => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition failed",
            staleRole.Message),

        // A role name another role in this workspace already holds. 409, not 412:
        // two roles cannot share a name however recently the caller read the
        // workspace, so re-reading and retrying unchanged would fail again. The
        // caller must choose a different name.
        TenantRoleNameAlreadyExistsException roleName => (
            StatusCodes.Status409Conflict,
            "Role name already exists",
            roleName.Message),

        // A member's access is not at the version the caller said it was
        // changing. 412, for the reason every other stale write gets one: the
        // operation may be perfectly fine and the caller has simply not seen this
        // person's roles as they stand.
        TenantMemberVersionMismatchException staleMember => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition failed",
            staleMember.Message),

        // An operation this platform will not perform on this member, whoever
        // asks: the Owner's access is not administered from team management, and
        // nobody edits their own. 409 rather than 403, because more permission
        // would not help -- it is a rule about the target, not about the caller.
        ProtectedMembershipException protectedMember => (
            StatusCodes.Status409Conflict,
            "Membership protected",
            protectedMember.Message),

        // The address invited is already on the team. 409: the request is
        // well-formed and permitted, and the workspace is simply already in the
        // state it asks for.
        AlreadyAMemberException alreadyMember => (
            StatusCodes.Status409Conflict,
            "Already a member",
            alreadyMember.Message),

        // One open invitation per address. 409, not 412: re-reading changes
        // nothing, and the caller must revoke the existing one or wait.
        InvitationAlreadyOpenException openInvitation => (
            StatusCodes.Status409Conflict,
            "Invitation already open",
            openInvitation.Message),

        // A real invitation that has been used, withdrawn, expired, or whose
        // issuing authority no longer stands. 410 rather than 404, because the
        // distinction is useful to the person holding the link: 404 would have
        // them checking it for typos, and this tells them to ask for a new one.
        // It discloses nothing -- presenting the token is already proof of having
        // been sent it.
        InvitationNoLongerUsableException unusable => (
            StatusCodes.Status410Gone,
            "Invitation no longer available",
            unusable.Message),

        // Signed in as somebody other than the invited address. 403: the caller is
        // authenticated and the invitation is real, and the refusal is about who
        // they are rather than about whether the invitation exists. It names
        // neither address.
        InvitationNotForThisAccountException wrongAccount => (
            StatusCodes.Status403Forbidden,
            "Invitation is for a different account",
            wrongAccount.Message),

        // A conversation was not at the version the caller said it was. 412, like
        // every other stale conditional write: two people in the same inbox is
        // the normal case, and the second to press a button is told rather than
        // silently undoing the first.
        ConversationVersionMismatchException staleConversation => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition failed",
            staleConversation.Message),

        // Somebody tried to reply to a conversation the AI is still answering.
        // 409: the request is well-formed and permitted, and contradicts the
        // state of the conversation. Not 403, which would suggest the person is
        // not allowed to reply at all -- they are, once they take it over.
        ConversationNotHandledByHumanException notHuman => (
            StatusCodes.Status409Conflict,
            "Conversation is answered by the AI",
            notHuman.Message),

        // A conversation was routed to somebody who cannot take it. 422 rather
        // than 400: the request is syntactically fine and names a real person,
        // and what is wrong is a fact about the workspace -- which is exactly
        // what the message explains, because an administrator can fix it.
        ConversationAssigneeIneligibleException ineligible => (
            StatusCodes.Status422UnprocessableEntity,
            "Cannot assign this conversation",
            ineligible.Message),

        // Live updates are not configured on this deployment. 503, like every
        // other missing dependency: the chat itself is unaffected, and the
        // client falls back to reading on demand.
        RealtimeUnavailableException realtime => (
            StatusCodes.Status503ServiceUnavailable,
            "Live updates unavailable",
            realtime.Message),

        // A website chat endpoint was not at the version the caller said it was.
        // 412, like every other stale conditional write.
        WebChatEndpointVersionMismatchException staleEndpoint => (
            StatusCodes.Status412PreconditionFailed,
            "Precondition failed",
            staleEndpoint.Message),

        // The agent named for a chat link cannot carry one -- it belongs to
        // another workspace, or already has a link. 409: the request is
        // well-formed and permitted, and contradicts the state of the workspace.
        WebChatAgentUnusableException agentUnusable => (
            StatusCodes.Status409Conflict,
            "AI agent unavailable",
            agentUnusable.Message),

        // A visitor sent the same message twice while the first was still being
        // answered, or re-sent one that already failed. 409 rather than an error:
        // nothing is broken, and the caller must not be encouraged to retry --
        // every reply costs the workspace money.
        WebChatTurnInProgressException inProgress => (
            StatusCodes.Status409Conflict,
            "Message already being answered",
            inProgress.Message),

        WebChatTurnAlreadyFailedException alreadyFailed => (
            StatusCodes.Status409Conflict,
            "Message already attempted",
            alreadyFailed.Message),

        // A public caller asking too often. 429, which is exactly what it means,
        // and paired with a Retry-After above.
        //
        // Deliberately not 503: the service is perfectly healthy and this caller
        // specifically is being asked to slow down. Deliberately not 403 either,
        // which would say the request is not permitted -- it is, just not yet.
        //
        // The detail is the exception's fixed sentence and names no quota, window,
        // counter, workspace or address. A caller learns that they should wait and
        // roughly how long, and nothing about the policy they are hitting.
        PublicChatRateLimitedException rateLimited => (
            StatusCodes.Status429TooManyRequests,
            "Too many requests",
            rateLimited.Message),

        // A public write that could not be protected, so it was not performed.
        // 503, and it is the honest one: the platform could have done the work and
        // deliberately declined because it could not tell whether it should.
        //
        // Same status and same wording as a chat that is switched off, which is
        // intentional -- a visitor must not be able to tell an unreachable counter
        // store from a withdrawn endpoint, because the difference is a fact about
        // this deployment's internals. The operator's copy of the difference is in
        // the log.
        PublicChatProtectionUnavailableException unprotected => (
            StatusCodes.Status503ServiceUnavailable,
            "Chat unavailable",
            unprotected.Message),

        // The chat is not serving the public: a draft or withdrawn endpoint, or an
        // agent that is not active. 503 -- it is a statement about this service
        // being unavailable right now, not about the request being wrong, and the
        // message deliberately does not say which of the causes applies.
        WebChatUnavailableException chatUnavailable => (
            StatusCodes.Status503ServiceUnavailable,
            "Chat unavailable",
            chatUnavailable.Message),

        // A use case that needs a signed-in person was reached without one. 401,
        // never 404: the caller has not yet said who they are, and signing in may
        // change the answer entirely -- which is the opposite of what a not-found
        // would tell them.
        //
        // This should be unreachable, because every protected endpoint is behind
        // authorization already. It is the second layer, for the case where a
        // route is mapped without its requirement.
        NotAuthenticatedException unauthenticated => (
            StatusCodes.Status401Unauthorized,
            "Not authenticated",
            unauthenticated.Message),

        // A listing request asking for a page this platform will not serve.
        // 400, because the caller can fix it: the message names the bound, so a
        // corrected request is one edit away. Refused rather than clamped --
        // silently returning fewer rows than asked for is how a client concludes
        // it has reached the end of a list when it has not.
        AiAgentPageValidationException paging => (
            StatusCodes.Status400BadRequest,
            "Invalid page request",
            paging.Message),

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
