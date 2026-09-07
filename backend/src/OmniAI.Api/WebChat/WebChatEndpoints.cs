using OmniAI.Api.Http;
using OmniAI.Api.Security;
using OmniAI.Application.Concurrency;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.CreateWebChatEndpoint;
using OmniAI.Application.WebChat.GetWebChatEndpointById;
using OmniAI.Application.WebChat.ListWebChatEndpoints;
using OmniAI.Application.WebChat.SetWebChatEndpointStatus;

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
            .WithName("CreateWebChatEndpoint");

        workspace.MapGet("/{webChatEndpointId:guid}", GetAsync)
            .WithName(GetEndpointRouteName);

        // POST to a named action rather than PATCH on a status field. Publishing a
        // chat endpoint to the internet and withdrawing it are decisions, not
        // property edits, and naming them keeps the audit of what was asked for
        // readable.
        workspace.MapPost("/{webChatEndpointId:guid}/activate", ActivateAsync)
            .WithName("ActivateWebChatEndpoint");

        workspace.MapPost("/{webChatEndpointId:guid}/disable", DisableAsync)
            .WithName("DisableWebChatEndpoint");

        return endpoints;
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
                detail: "A website chat link must name the AI agent that will answer on it.");
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
