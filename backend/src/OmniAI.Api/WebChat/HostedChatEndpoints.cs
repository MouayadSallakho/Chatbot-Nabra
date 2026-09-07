using OmniAI.Api.Security;
using OmniAI.Application.WebChat.Hosted;

namespace OmniAI.Api.WebChat;

/// <summary>
/// The hosted chat page's own API: anonymous, same-origin, and narrow.
/// </summary>
/// <remarks>
/// <strong>Anonymous is not the same as unprotected.</strong> Every write here
/// still carries the platform's antiforgery token, because these are still
/// browser requests made from a page on this origin and a cross-site page can
/// still cause a browser to send them. Exempting them because "there is no
/// session to steal" would be wrong twice over: there is a session — the
/// conversation cookie — and it is exactly the thing a cross-site request would
/// be riding on.
/// <para>
/// <strong>The visitor's credential lives in a cookie the page cannot read.</strong>
/// It is issued <c>HttpOnly</c>, scoped to this route prefix, and never appears in
/// a response body, a URL or a log. Script on the hosted page never needs it: the
/// browser attaches it to these requests and to nothing else. That is the whole
/// reason the credential is not in <c>localStorage</c>, where any script on the
/// origin — including one injected by a future mistake — could read it.
/// </para>
/// <para>
/// <strong>This is a same-origin design, deliberately.</strong> A cookie scoped to
/// this path works because the chat page is served from the same origin as the
/// API. A third-party embeddable widget runs on somebody else's site and cannot
/// use this at all; it will need its own cross-origin transport, decided with the
/// widget. Nothing here is weakened in advance to accommodate it.
/// </para>
/// </remarks>
internal static class HostedChatEndpoints
{
    /// <summary>The cookie carrying one visitor's conversation credential.</summary>
    internal const string SessionCookieName = "omniai.webchat";

    /// <summary>
    /// The only path the credential is sent to.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose. The browser attaches this cookie to the hosted chat's
    /// own API and to nothing else — not to the admin API, not to the sign-in
    /// routes, not to the page itself. A credential that is never sent where it is
    /// not needed cannot be captured there either.
    /// </remarks>
    internal const string SessionCookiePath = "/api/public/webchat";

    public static IEndpointRouteBuilder MapHostedChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder chat = endpoints
            .MapGroup(SessionCookiePath)
            .WithTags("Hosted chat")
            .AllowAnonymous();

        chat.MapGet("/{publicKey}", GetConfigAsync)
            .WithName("GetPublicWebChat");

        chat.MapPost("/{publicKey}/conversations", StartConversationAsync)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .WithName("StartWebChatConversation");

        chat.MapGet("/conversations/{conversationId:guid}/messages", ListMessagesAsync)
            .WithName("ListWebChatMessages");

        chat.MapPost("/conversations/{conversationId:guid}/messages", SendMessageAsync)
            .AddEndpointFilter<AntiforgeryEndpointFilter>()
            .WithName("SendWebChatMessage");

        return endpoints;
    }

    /// <summary><c>GET /api/public/webchat/{publicKey}</c>.</summary>
    /// <remarks>
    /// The only thing a stranger learns from a public key: a display name and
    /// whether the chat is open. No workspace, no agent id, no instructions.
    /// </remarks>
    private static async Task<IResult> GetConfigAsync(
        string publicKey,
        StartWebChatConversationHandler handler,
        CancellationToken cancellationToken)
    {
        PublicWebChatView? view = await handler
            .GetAsync(new GetPublicWebChatQuery(publicKey), cancellationToken)
            .ConfigureAwait(false);

        return view is null
            ? Results.NotFound()
            : Results.Ok(PublicWebChatResponse.From(view));
    }

    /// <summary><c>POST /api/public/webchat/{publicKey}/conversations</c>.</summary>
    private static async Task<IResult> StartConversationAsync(
        string publicKey,
        HttpContext http,
        StartWebChatConversationHandler handler,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        StartedWebChatSession? started = await handler
            .StartAsync(new StartWebChatConversationCommand(publicKey), cancellationToken)
            .ConfigureAwait(false);

        if (started is null)
        {
            return Results.NotFound();
        }

        // The one moment the raw credential exists outside the visitor's browser.
        // It goes into a cookie and nowhere else -- not into the body below, which
        // has no field for it.
        http.Response.Cookies.Append(
            SessionCookieName,
            started.AccessToken,
            SessionCookieOptions(environment));

        // The credential must never be cached by anything between here and the
        // browser.
        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(
            new StartedConversationResponse(
                started.Conversation.ConversationId,
                started.Conversation.AgentName));
    }

    /// <summary><c>GET /api/public/webchat/conversations/{conversationId}/messages</c>.</summary>
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
                new ListWebChatMessagesQuery(conversationId, ReadCredential(http), after, limit),
                cancellationToken)
            .ConfigureAwait(false);

        if (page is null)
        {
            return Results.NotFound();
        }

        // A transcript is somebody's private conversation. Nothing between here
        // and their browser may keep a copy.
        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(
            new ChatMessagePageResponse(
                [.. page.Items.Select(ChatMessageResponse.From)],
                page.NextCursor));
    }

    /// <summary><c>POST /api/public/webchat/conversations/{conversationId}/messages</c>.</summary>
    private static async Task<IResult> SendMessageAsync(
        Guid conversationId,
        SendChatMessageRequest? request,
        HttpContext http,
        SendWebChatMessageHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        WebChatTurnResult? turn = await handler
            .HandleAsync(
                new SendWebChatMessageCommand(
                    conversationId,
                    ReadCredential(http),
                    request?.ClientMessageId,
                    request?.Text),
                cancellationToken)
            .ConfigureAwait(false);

        if (turn is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(
            new ChatTurnResponse(
                ChatMessageResponse.From(turn.CustomerMessage),
                ChatMessageResponse.From(turn.Reply)));
    }

    /// <summary>
    /// The visitor's credential, from the cookie and from nowhere else.
    /// </summary>
    /// <remarks>
    /// Deliberately not read from a header, a query parameter or the body. If it
    /// could arrive in a URL it would end up in access logs and browser history;
    /// if it could arrive in a header, script on the page would have to be able to
    /// read it, which is exactly what <c>HttpOnly</c> is preventing.
    /// </remarks>
    private static string? ReadCredential(HttpContext http) =>
        http.Request.Cookies.TryGetValue(SessionCookieName, out string? token) ? token : null;

    /// <summary>
    /// How the credential cookie is written.
    /// </summary>
    /// <remarks>
    /// <para><c>HttpOnly</c>: script never needs it, so script never gets it.</para>
    /// <para>
    /// <c>Secure</c> everywhere but Development, matching the session cookie
    /// exactly. Development is served over plain HTTP on a loopback address, and a
    /// cookie the browser refuses to send there would make the whole feature
    /// untestable locally; nothing else relaxes.
    /// </para>
    /// <para>
    /// <c>SameSite=Lax</c>, the same as the platform's other cookies. The hosted
    /// page is same-origin with this API, so <c>Lax</c> costs nothing and keeps a
    /// cross-site form post from carrying somebody's conversation credential.
    /// </para>
    /// <para>
    /// Session-scoped, with no expiry. A visitor's conversation lasts as long as
    /// their browser session; persisting the credential to disk would leave a
    /// stranger's conversation reachable on a shared computer long after they had
    /// walked away, which is a worse failure than having to start a new chat.
    /// </para>
    /// </remarks>
    private static CookieOptions SessionCookieOptions(IHostEnvironment environment) =>
        new()
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = SessionCookiePath,
            IsEssential = true,
        };
}
