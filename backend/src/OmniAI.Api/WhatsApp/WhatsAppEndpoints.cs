using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WhatsApp;
using OmniAI.Application.WhatsApp.Persistence;
using OmniAI.Api.ErrorHandling;

namespace OmniAI.Api.WhatsApp;

/// <summary>
/// Administering a workspace's WhatsApp connections.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reading and managing are separate permissions.</strong> Seeing how a
/// number is connected is <c>whatsapp.view</c>; connecting one, storing its
/// credential and putting it in or out of service is <c>whatsapp.manage</c>.
/// Neither implies the other, and neither says anything about conversations —
/// somebody who administers the channel does not thereby gain the right to read
/// what customers said on it.
/// </para>
/// <para>
/// <strong>No response here carries an access token.</strong> Not redacted, not
/// masked: absent. The view these responses are built from has no field for one,
/// and neither does the aggregate behind it.
/// </para>
/// <para>
/// <strong>Every write is conditional.</strong> Changing which number a
/// connection uses, or which agent answers on it, applied to a version somebody
/// else has already replaced is a change that did not do what the person
/// intended — and here that means customer messages going to the wrong agent.
/// </para>
/// </remarks>
internal static class WhatsAppEndpoints
{
    private const string GetConnectionRouteName = "GetWhatsAppConnection";

    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder workspace = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/whatsapp/connections")
            .WithTags("WhatsApp")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        workspace.MapGet("/", ListAsync)
            .WithName("ListWhatsAppConnections");

        workspace.MapPost("/", CreateAsync)
            .WithName("CreateWhatsAppConnection")
            .Produces<WhatsAppConnectionResponse>(StatusCodes.Status201Created);

        workspace.MapGet("/{connectionId:guid}", GetAsync)
            .WithName(GetConnectionRouteName);

        workspace.MapPut("/{connectionId:guid}", UpdateAsync)
            .WithName("UpdateWhatsAppConnection")
            .Produces<WhatsAppConnectionResponse>(StatusCodes.Status200OK);

        // PUT to a sub-resource rather than a field on the connection. The token
        // is write-only, so it has no representation the connection could carry,
        // and a route of its own is what makes "who can send a credential here"
        // answerable by reading one line.
        workspace.MapPut("/{connectionId:guid}/credential", ReplaceCredentialAsync)
            .WithName("ReplaceWhatsAppCredential")
            .Produces<WhatsAppConnectionResponse>(StatusCodes.Status200OK);

        // POST to a named action rather than PATCH on a status field. Putting a
        // number into service and taking it out are decisions, not property
        // edits, and naming them keeps the audit readable.
        workspace.MapPost("/{connectionId:guid}/activate", ActivateAsync)
            .WithName("ActivateWhatsAppConnection")
            .Produces<WhatsAppConnectionResponse>(StatusCodes.Status200OK);

        workspace.MapPost("/{connectionId:guid}/disable", DisableAsync)
            .WithName("DisableWhatsAppConnection")
            .Produces<WhatsAppConnectionResponse>(StatusCodes.Status200OK);

        // The WhatsApp side of one conversation, for the shared inbox. Its own
        // group because it is guarded by conversations.view rather than by a
        // WhatsApp permission: this is conversation content, and it must be
        // readable by exactly the people who may read the transcript.
        endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/whatsapp/conversations")
            .WithTags("WhatsApp")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .MapGet("/{conversationId:guid}", GetConversationAsync)
            .WithName("GetWhatsAppConversation");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /api/tenants/{tenantId}/whatsapp/conversations/{conversationId}</c>.
    /// </summary>
    private static async Task<IResult> GetConversationAsync(
        Guid tenantId,
        Guid conversationId,
        ReadWhatsAppConversationHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        WhatsAppConversationFacts? facts = await handler
            .HandleAsync(
                new ReadWhatsAppConversationQuery(tenantId, conversationId),
                cancellationToken)
            .ConfigureAwait(false);

        // A conversation that did not come from WhatsApp answers the same
        // not-found as one that does not exist. The inbox asks this only for
        // conversations whose channel says WhatsApp, so the case is a race or a
        // hand-written request, and neither is owed an explanation.
        return facts is null
            ? Results.NotFound()
            : Results.Ok(WhatsAppConversationResponse.From(facts));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/whatsapp/connections</c>.</summary>
    private static async Task<IResult> ListAsync(
        Guid tenantId,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        IReadOnlyList<WhatsAppConnectionRow>? connections = await handlers
            .ListAsync(new ListWhatsAppConnectionsQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return connections is null
            ? Results.NotFound()
            : Results.Ok(connections.Select(WhatsAppConnectionResponse.From).ToArray());
    }

    /// <summary><c>GET /api/tenants/{tenantId}/whatsapp/connections/{id}</c>.</summary>
    private static async Task<IResult> GetAsync(
        Guid tenantId,
        Guid connectionId,
        HttpContext http,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        Versioned<WhatsAppConnectionView>? connection = await handlers
            .GetAsync(new GetWhatsAppConnectionQuery(tenantId, connectionId), cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, connection);
    }

    /// <summary><c>POST /api/tenants/{tenantId}/whatsapp/connections</c>.</summary>
    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreateWhatsAppConnectionRequest? request,
        HttpContext http,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        if (request?.AiAgentId is not Guid agentId)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "AI agent is required",
                detail: "A WhatsApp connection must name the AI agent that will answer on it.",
                extensions: new Dictionary<string, object?> { [ProblemCodes.Member] = "whatsapp.agent.required" });
        }

        Versioned<WhatsAppConnectionView>? created = await handlers
            .CreateAsync(
                new CreateWhatsAppConnectionCommand(
                    tenantId,
                    agentId,
                    request.WabaId,
                    request.PhoneNumberId,
                    request.DisplayPhoneNumber),
                cancellationToken)
            .ConfigureAwait(false);

        if (created is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = EntityTags.Format(created.Version);

        return Results.CreatedAtRoute(
            GetConnectionRouteName,
            new { tenantId, connectionId = created.Value.Id },
            WhatsAppConnectionResponse.From(created.Value));
    }

    /// <summary><c>PUT /api/tenants/{tenantId}/whatsapp/connections/{id}</c>.</summary>
    private static async Task<IResult> UpdateAsync(
        Guid tenantId,
        Guid connectionId,
        UpdateWhatsAppConnectionRequest? request,
        HttpContext http,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        if (request?.AiAgentId is not Guid agentId)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "AI agent is required",
                detail: "A WhatsApp connection must name the AI agent that will answer on it.",
                extensions: new Dictionary<string, object?> { [ProblemCodes.Member] = "whatsapp.agent.required" });
        }

        Versioned<WhatsAppConnectionView>? updated = await handlers
            .UpdateAsync(
                new UpdateWhatsAppConnectionCommand(
                    tenantId,
                    connectionId,
                    agentId,
                    request.WabaId,
                    request.PhoneNumberId,
                    request.DisplayPhoneNumber,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, updated);
    }

    /// <summary>
    /// <c>PUT /api/tenants/{tenantId}/whatsapp/connections/{id}/credential</c>.
    /// </summary>
    /// <remarks>
    /// The one route on this platform that receives a WhatsApp access token. It
    /// is encrypted before it is stored and is never returned; the response is the
    /// connection, which now says a credential is configured.
    /// </remarks>
    private static async Task<IResult> ReplaceCredentialAsync(
        Guid tenantId,
        Guid connectionId,
        ReplaceWhatsAppCredentialRequest? request,
        HttpContext http,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<WhatsAppConnectionView>? updated = await handlers
            .ReplaceCredentialAsync(
                new ReplaceWhatsAppCredentialCommand(
                    tenantId,
                    connectionId,
                    request?.AccessToken,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, updated);
    }

    /// <summary><c>POST .../connections/{id}/activate</c>.</summary>
    private static Task<IResult> ActivateAsync(
        Guid tenantId,
        Guid connectionId,
        HttpContext http,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, connectionId, activate: true, http, handlers, cancellationToken);

    /// <summary><c>POST .../connections/{id}/disable</c>.</summary>
    private static Task<IResult> DisableAsync(
        Guid tenantId,
        Guid connectionId,
        HttpContext http,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, connectionId, activate: false, http, handlers, cancellationToken);

    private static async Task<IResult> SetStatusAsync(
        Guid tenantId,
        Guid connectionId,
        bool activate,
        HttpContext http,
        WhatsAppConnectionHandlers handlers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(handlers);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<WhatsAppConnectionView>? updated = await handlers
            .SetStatusAsync(
                new SetWhatsAppConnectionStatusCommand(tenantId, connectionId, activate, expected),
                cancellationToken)
            .ConfigureAwait(false);

        return Respond(http, updated);
    }

    /// <summary>The connection with its current entity tag, or a not-found.</summary>
    private static IResult Respond(HttpContext http, Versioned<WhatsAppConnectionView>? connection)
    {
        if (connection is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = EntityTags.Format(connection.Version);

        return Results.Ok(WhatsAppConnectionResponse.From(connection.Value));
    }
}
