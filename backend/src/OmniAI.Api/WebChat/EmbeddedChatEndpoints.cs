using OmniAI.Api.Security;
using OmniAI.Application.WebChat.Embedded;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.WebChat;
using OmniAI.Application.Conversations.Realtime;

namespace OmniAI.Api.WebChat;

/// <summary>
/// The transport the embedded widget speaks, kept deliberately apart from the
/// hosted one.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two headers carry everything that is different about this transport.</strong>
/// <c>X-OmniAI-Embed-Origin</c> is the website the browser told the iframe it is
/// embedded on; <c>X-OmniAI-Embed-Session</c> is the conversation credential the
/// iframe holds. Both are explicit. Neither is a cookie, and that is the whole
/// design: a browser that blocks third-party cookies — which is most of them now —
/// would otherwise break every embedded widget, and the fix for that must never be
/// to ask a customer to weaken their browser.
/// </para>
/// <para>
/// <strong>Why these routes carry no antiforgery token, and are not weaker for
/// it.</strong> Antiforgery exists to stop a hostile page riding <em>ambient</em>
/// authority — a cookie the browser attaches by itself. Nothing here is ambient:
/// the credential is a value the iframe read from a response and keeps in its own
/// origin's memory, and a cross-site page cannot read it. Beyond that, every one
/// of these routes requires a custom request header, which is not a
/// CORS-simple header: a cross-origin page attempting one triggers a preflight,
/// and this API answers no cross-origin preflight at all. The request never
/// leaves the browser. The hosted routes keep their antiforgery filter exactly as
/// Phase 15 and 16 left it.
/// </para>
/// <para>
/// <strong>The origin is trusted because a browser produced it, not because a
/// page said so.</strong> The iframe learns it from <c>postMessage</c>'s
/// <c>event.origin</c>, which the browser fills in and a page cannot forge, and
/// forwards it here. That makes this an embedding policy: it decides which pages a
/// browser will let run the widget. It is not, and is not documented as, a defence
/// against a script speaking HTTP directly — that is what the Phase 16 distributed
/// protection is for, and every route below passes through it unchanged.
/// </para>
/// </remarks>
internal static class EmbeddedChatEndpoints
{
    /// <summary>The website the browser reported to the iframe.</summary>
    internal const string OriginHeaderName = "X-OmniAI-Embed-Origin";

    /// <summary>The conversation credential the iframe holds.</summary>
    internal const string SessionHeaderName = "X-OmniAI-Embed-Session";

    internal const string RoutePrefix = "/api/public/embed";

    public static IEndpointRouteBuilder MapEmbeddedChatEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder embed = endpoints
            .MapGroup(RoutePrefix)
            .WithTags("Embedded chat")
            .AllowAnonymous();

        embed.MapGet("/{publicKey}", GetConfigAsync)
            .WithName("GetEmbeddedWebChat")
            .WithSummary("An approved widget's configuration and the workspace's name. Every refusal is one indistinguishable 404. Optional lang=en|ar (any letter case, Phase 39.1): greeting is that language's own when the workspace set one, else the fallback; an absent or unknown lang gives the fallback, as before.")
            .Produces<EmbeddedChatConfigResponse>()
            .Produces(StatusCodes.Status404NotFound);

        embed.MapPost("/{publicKey}/conversations", StartConversationAsync)
            .WithName("StartEmbeddedWebChatConversation")
            .Produces<StartedEmbeddedConversationResponse>(StatusCodes.Status200OK);

        // Exchanges the embedded credential for a realtime ticket.
        //
        // No antiforgery filter, exactly like the other embed routes and for the
        // same reason: the credential is not ambient, and the custom header this
        // needs forces a preflight the API answers for nobody. There is nothing
        // for a forged cross-site request to ride on.
        //
        // The allowlist is re-checked inside, on this request, so a website
        // removed while a widget is open cannot mint a ticket a second later.
        embed.MapPost("/conversations/{conversationId:guid}/realtime-ticket", IssueTicketAsync)
            .WithName("IssueEmbeddedRealtimeTicket")
            .Produces<RealtimeTicketResponse>(StatusCodes.Status200OK);

        embed.MapGet("/conversations/{conversationId:guid}/messages", ListMessagesAsync)
            .WithName("ListEmbeddedWebChatMessages");

        embed.MapPost("/conversations/{conversationId:guid}/messages", SendMessageAsync)
            .WithName("SendEmbeddedWebChatMessage")
            .Produces<ChatTurnResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>
    /// Tells an approved widget what it is and how it should look.
    /// </summary>
    /// <remarks>
    /// Creates nothing at all. A customer's page loading, and a visitor opening the
    /// widget, both end here with no conversation, no session and no credential.
    /// </remarks>
    private static async Task<IResult> GetConfigAsync(
        string publicKey,
        string? lang,
        HttpContext http,
        EmbeddedWebChatHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        EmbeddedWebChatView? view = await handler
            .GetAsync(
                new GetEmbeddedWebChatQuery(publicKey, ReadOrigin(http), PageLanguage.Read(lang)),
                cancellationToken)
            .ConfigureAwait(false);

        if (view is null)
        {
            return NotAvailable();
        }

        // The appearance depends on the workspace's current configuration, and a
        // revoked website must stop working immediately rather than after a cache
        // expires.
        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new EmbeddedChatConfigResponse(
            view.AgentName,
            view.IsAvailable,
            view.Greeting,
            view.AccentColor,
            view.LauncherPosition,
            view.Theme,
            view.WorkspaceName));
    }

    /// <summary>
    /// Opens a conversation and hands the iframe its credential, once.
    /// </summary>
    /// <remarks>
    /// The response is the only place the raw credential ever exists outside the
    /// browser holding it, which is why it is explicitly not storable by anything
    /// in between.
    /// </remarks>
    private static async Task<IResult> StartConversationAsync(
        string publicKey,
        HttpContext http,
        EmbeddedWebChatHandler handler,
        IClientNetworkKeys networkKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        StartedEmbeddedConversation? started = await handler
            .StartAsync(
                new StartEmbeddedConversationCommand(
                    publicKey,
                    ReadOrigin(http),
                    ClientNetworkIdentity.Resolve(http, networkKeys)),
                cancellationToken)
            .ConfigureAwait(false);

        if (started is null)
        {
            return NotAvailable();
        }

        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(new StartedEmbeddedConversationResponse(
            started.ConversationId,
            started.AgentName,
            started.Credential));
    }

    private static async Task<IResult> ListMessagesAsync(
        Guid conversationId,
        long? after,
        int? limit,
        HttpContext http,
        ListWebChatMessagesHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        PublicChatMessagePage? page = await handler
            .HandleAsync(
                new ListWebChatMessagesQuery(
                    conversationId,
                    ReadCredential(http),
                    after,
                    limit,
                    WebChatClientMode.Embedded),
                cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return NotAvailable();
        }

        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(
            new ChatMessagePageResponse(
                [.. page.Items.Select(ChatMessageResponse.From)],
                page.NextCursor,
                page.Handling));
    }

    /// <summary>
    /// Sends one message, through the same runtime the hosted page uses.
    /// </summary>
    /// <remarks>
    /// The handler is the Phase 15 one, unchanged: protection, idempotency, the
    /// conversation lock, the generation budget, the customer's message, optional
    /// retrieval, then generation. The only thing this route decides is which
    /// transport is presenting a credential.
    /// </remarks>
    private static async Task<IResult> SendMessageAsync(
        Guid conversationId,
        SendChatMessageRequest? request,
        HttpContext http,
        SendWebChatMessageHandler handler,
        IClientNetworkKeys networkKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        WebChatTurnResult? turn = await handler
            .HandleAsync(
                new SendWebChatMessageCommand(
                    conversationId,
                    ReadCredential(http),
                    request?.ClientMessageId,
                    request?.Text,
                    ClientNetworkIdentity.Resolve(http, networkKeys),
                    WebChatClientMode.Embedded),
                cancellationToken)
            .ConfigureAwait(false);

        if (turn is null)
        {
            return NotAvailable();
        }

        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(
            new ChatTurnResponse(
                ChatMessageResponse.From(turn.CustomerMessage),
                ChatMessageResponse.FromOrNull(turn.Reply),
                turn.Handling));
    }

    /// <summary>The website the iframe says the browser reported.</summary>
    private static string? ReadOrigin(HttpContext http) =>
        http.Request.Headers.TryGetValue(OriginHeaderName, out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;

    /// <summary>
    /// The conversation credential, from a header and never from a cookie.
    /// </summary>
    /// <remarks>
    /// Deliberately not reading the hosted cookie as a fallback. A fallback would
    /// mean a hosted credential could authenticate an embedded request, which is
    /// exactly the confusion the two modes exist to prevent.
    /// </remarks>
    private static string? ReadCredential(HttpContext http) =>
        http.Request.Headers.TryGetValue(SessionHeaderName, out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;

    /// <summary>
    /// <c>POST /api/public/embed/conversations/{id}/realtime-ticket</c>.
    /// </summary>
    /// <remarks>
    /// <strong>The embedded credential is readable by script, and that is
    /// precisely why it must not travel in a URL.</strong> It lives in the
    /// iframe's own <c>sessionStorage</c> and goes out in a header on every
    /// request; a websocket address cannot carry a header, so without this
    /// exchange the credential would end up in a query string that proxies log
    /// and the embedding page's own network panel shows.
    /// <para>
    /// A hosted ticket cannot be minted here and an embedded ticket cannot be
    /// minted on the hosted route: the transport is part of what is checked, and
    /// part of what the ticket records.
    /// </para>
    /// </remarks>
    private static async Task<IResult> IssueTicketAsync(
        Guid conversationId,
        HttpContext http,
        IssueRealtimeTicketHandler handler,
        CancellationToken cancellationToken)
    {
        RealtimeTicket? ticket = await handler
            .HandleAsync(
                new IssueRealtimeTicketCommand(
                    conversationId,
                    ReadCredential(http),
                    WebChatClientMode.Embedded),
                cancellationToken)
            .ConfigureAwait(false);

        return ticket is null
            ? NotAvailable()
            : Results.Ok(new RealtimeTicketResponse(ticket.Value, ticket.ExpiresInSeconds));
    }

    /// <summary>
    /// One answer for every reason a widget may not run here.
    /// </summary>
    /// <remarks>
    /// An unknown public key, a malformed one, a website nobody approved, a
    /// website that was approved and has since been removed, and a credential that
    /// does not match all produce this. Telling them apart would let somebody
    /// holding a public key discover which websites a workspace has authorised —
    /// which is precisely the list they would need in order to attack it.
    /// </remarks>
    private static IResult NotAvailable() => Results.NotFound();
}
