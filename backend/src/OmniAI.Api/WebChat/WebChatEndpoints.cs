using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat.GetWebChatWidget;
using OmniAI.Application.WebChat.UpdateWebChatWidget;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.CreateWebChatEndpoint;
using OmniAI.Application.WebChat.GetWebChatEndpointById;
using OmniAI.Application.WebChat.ListWebChatEndpoints;
using OmniAI.Application.WebChat.SetWebChatEndpointStatus;
using OmniAI.Api.ErrorHandling;

namespace OmniAI.Api.WebChat;

/// <summary>
/// Administering a workspace's website chat.
/// </summary>
/// <remarks>
/// <strong>Reading and publishing are separate permissions.</strong> Seeing how a
/// chat endpoint is configured is <c>website-chat.view</c>; creating one and
/// deciding whether the public can use it is <c>website-chat.manage</c>. Neither
/// implies the other, and neither implies anything about conversations — somebody
/// who administers the channel does not thereby gain the right to read what
/// customers said on it.
/// <para>
/// <strong>Every route here is authenticated and antiforgery-checked.</strong>
/// The public surface is a different file, mapped separately, and shares nothing
/// with this one but the database.
/// </para>
/// </remarks>
internal static class WebChatEndpoints
{
    private const string GetEndpointRouteName = "GetWebChatEndpointById";

    public static IEndpointRouteBuilder MapWebChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder workspace = endpoints
            .MapGroup("/api/tenants/{tenantId:guid}/website-chat")
            .WithTags("Website chat")
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        workspace.MapGet("/", ListAsync)
            .WithName("ListWebChatEndpoints");

        workspace.MapPost("/", CreateAsync)
            .WithName("CreateWebChatEndpoint")
            .Produces<WebChatEndpointResponse>(StatusCodes.Status201Created);

        workspace.MapGet("/{webChatEndpointId:guid}", GetAsync)
            .WithName(GetEndpointRouteName);

        // POST to a named action rather than PATCH on a status field. Publishing a
        // chat endpoint to the internet and withdrawing it are decisions, not
        // property edits, and naming them keeps the audit of what was asked for
        // readable.
        workspace.MapPost("/{webChatEndpointId:guid}/activate", ActivateAsync)
            .WithName("ActivateWebChatEndpoint")
            .Produces<WebChatEndpointResponse>(StatusCodes.Status200OK);

        workspace.MapPost("/{webChatEndpointId:guid}/disable", DisableAsync)
            .WithName("DisableWebChatEndpoint")
            .Produces<WebChatEndpointResponse>(StatusCodes.Status200OK);

        // The widget's appearance and the websites it may be embedded on. Inside
        // the same authenticated, antiforgery-checked group as everything else
        // here: deciding where a widget may run is workspace administration, not
        // something the public surface exposes.
        workspace.MapGet("/{webChatEndpointId:guid}/widget", GetWidgetAsync)
            .WithName("GetWebChatWidget");

        workspace.MapPut("/{webChatEndpointId:guid}/widget", UpdateWidgetAsync)
            .WithName("UpdateWebChatWidget")
            .Produces<WebChatWidgetConfigurationResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Reads a widget's appearance and the websites it may be embedded on.</summary>
    private static async Task<IResult> GetWidgetAsync(
        HttpContext http,
        Guid tenantId,
        Guid webChatEndpointId,
        GetWebChatWidgetHandler handler,
        CancellationToken cancellationToken)
    {
        Versioned<WebChatWidgetConfigurationView>? widget = await handler
            .HandleAsync(
                new GetWebChatWidgetQuery(tenantId, webChatEndpointId),
                cancellationToken)
            .ConfigureAwait(false);

        return RespondWithWidget(http, widget, webChatEndpointId);
    }

    /// <summary>
    /// Replaces a widget's appearance and the websites it may be embedded on.
    /// </summary>
    /// <remarks>
    /// Conditional like every other write here. Removing a website is a security
    /// action, and a security action applied to a version somebody else has already
    /// replaced is an action that did not do what the person intended.
    /// </remarks>
    private static async Task<IResult> UpdateWidgetAsync(
        HttpContext http,
        Guid tenantId,
        Guid webChatEndpointId,
        UpdateWebChatWidgetRequest request,
        UpdateWebChatWidgetHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<WebChatWidgetConfigurationView>? updated = await handler
            .HandleAsync(
                new UpdateWebChatWidgetCommand(
                    tenantId,
                    webChatEndpointId,
                    request.Greeting,
                    request.AccentColor,
                    request.LauncherPosition,
                    request.Theme,
                    request.AllowedOrigins,
                    expected),
                cancellationToken)
            .ConfigureAwait(false);

        return RespondWithWidget(http, updated, webChatEndpointId);
    }

    private static IResult RespondWithWidget(
        HttpContext http,
        Versioned<WebChatWidgetConfigurationView>? widget,
        Guid webChatEndpointId)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (widget is null)
        {
            return Results.Problem(
                title: "Website chat endpoint not found",
                detail: $"This workspace has no website chat endpoint with id '{webChatEndpointId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        http.Response.Headers.ETag = EntityTags.Format(widget.Version);

        return Results.Ok(WebChatWidgetConfigurationResponse.From(widget.Value, http.Request));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/website-chat</c>.</summary>
    private static async Task<IResult> ListAsync(
        Guid tenantId,
        ListWebChatEndpointsHandler handler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WebChatEndpointView>? endpoints = await handler
            .HandleAsync(new ListWebChatEndpointsQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return endpoints is null
            ? Results.NotFound()
            : Results.Ok(endpoints.Select(WebChatEndpointResponse.From).ToArray());
    }

    /// <summary><c>POST /api/tenants/{tenantId}/website-chat</c>.</summary>
    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreateWebChatEndpointRequest? request,
        HttpContext http,
        CreateWebChatEndpointHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (request?.AiAgentId is not Guid agentId)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "AI agent is required",
                detail: "A website chat link must name the AI agent that will answer on it.",
                extensions: new Dictionary<string, object?> { [ProblemCodes.Member] = "website-chat.agent.required" });
        }

        Versioned<WebChatEndpointView>? created = await handler
            .HandleAsync(new CreateWebChatEndpointCommand(tenantId, agentId), cancellationToken)
            .ConfigureAwait(false);

        if (created is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = EntityTags.Format(created.Version);

        return Results.CreatedAtRoute(
            GetEndpointRouteName,
            new { tenantId, webChatEndpointId = created.Value.Id },
            WebChatEndpointResponse.From(created.Value));
    }

    /// <summary><c>GET /api/tenants/{tenantId}/website-chat/{webChatEndpointId}</c>.</summary>
    private static async Task<IResult> GetAsync(
        Guid tenantId,
        Guid webChatEndpointId,
        HttpContext http,
        GetWebChatEndpointByIdHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        Versioned<WebChatEndpointView>? endpoint = await handler
            .HandleAsync(
                new GetWebChatEndpointByIdQuery(tenantId, webChatEndpointId),
                cancellationToken)
            .ConfigureAwait(false);

        if (endpoint is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = EntityTags.Format(endpoint.Version);

        return Results.Ok(WebChatEndpointResponse.From(endpoint.Value));
    }

    /// <summary><c>POST /api/tenants/{tenantId}/website-chat/{id}/activate</c>.</summary>
    private static Task<IResult> ActivateAsync(
        Guid tenantId,
        Guid webChatEndpointId,
        HttpContext http,
        SetWebChatEndpointStatusHandler handler,
        CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, webChatEndpointId, activate: true, http, handler, cancellationToken);

    /// <summary><c>POST /api/tenants/{tenantId}/website-chat/{id}/disable</c>.</summary>
    private static Task<IResult> DisableAsync(
        Guid tenantId,
        Guid webChatEndpointId,
        HttpContext http,
        SetWebChatEndpointStatusHandler handler,
        CancellationToken cancellationToken) =>
        SetStatusAsync(tenantId, webChatEndpointId, activate: false, http, handler, cancellationToken);

    /// <summary>
    /// The conditional write both lifecycle routes share.
    /// </summary>
    /// <remarks>
    /// <c>If-Match</c> is read before the handler is reached, so a caller who has
    /// not read the endpoint is told to reload rather than being allowed to change
    /// something they have never seen. A stale tag becomes a 412 through the
    /// problem-details handler.
    /// </remarks>
    private static async Task<IResult> SetStatusAsync(
        Guid tenantId,
        Guid webChatEndpointId,
        bool activate,
        HttpContext http,
        SetWebChatEndpointStatusHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        if (EntityTags.ReadIfMatch(http.Request, out ConcurrencyToken expected) is IResult failure)
        {
            return failure;
        }

        Versioned<WebChatEndpointView>? updated = await handler
            .HandleAsync(
                new SetWebChatEndpointStatusCommand(tenantId, webChatEndpointId, activate, expected),
                cancellationToken)
            .ConfigureAwait(false);

        if (updated is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = EntityTags.Format(updated.Version);

        return Results.Ok(WebChatEndpointResponse.From(updated.Value));
    }
}
