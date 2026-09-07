using System.Globalization;
using OmniAI.Api.Security;
using OmniAI.Application.WebChat.Hosted;
using OmniAI.Application.WebChat.Protection;

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
/// <para>
/// <strong>One credential cookie per conversation, and the path is what makes
/// that possible.</strong> A single cookie scoped to the whole chat API could
/// only ever hold one conversation's secret, so starting a second conversation in
/// the same browser silently took away access to the first — somebody with two
/// tabs open lost one of them. Each credential is now written at the path of the
/// conversation it opens, so several coexist in one cookie jar and the browser
/// sends exactly the one that matches the conversation being addressed. The name
/// stays the same for all of them: putting the conversation id in the cookie name
/// would work too, but a name derived from the resource is not a name a server can
/// look up without already knowing the answer, and putting the <em>secret</em> in
/// a name would be indefensible.
/// </para>
/// <para>
/// <strong>The writes are still per-visitor isolated.</strong> Paths separate one
/// conversation from another inside one browser; they are not an authorisation
/// mechanism and are not treated as one. A credential is still only accepted for
/// the conversation whose stored digest it matches, so a visitor who copies
/// another conversation's cookie into the right path is still refused.
/// </para>
/// </remarks>
internal static class HostedChatEndpoints
{
    /// <summary>The cookie carrying one visitor's conversation credential.</summary>
    internal const string SessionCookieName = "omniai.webchat";

    /// <summary>The hosted chat's own API, and the group everything below is in.</summary>
    internal const string RoutePrefix = "/api/public/webchat";

    /// <summary>
    /// The path prefix earlier releases scoped the credential to.
    /// </summary>
    /// <remarks>
    /// Kept only so it can be deleted. A browser that talked to an earlier
    /// release still holds a cookie of the same name at this broader path, and
    /// that cookie also matches every per-conversation path — so a request would
    /// carry two cookies called <c>omniai.webchat</c> and the server would read
    /// whichever the browser listed first. Rather than reason about that ordering,
    /// the old cookie is expired the moment a conversation is started. One
    /// release's worth of tidying, with an explicit reason.
    /// </remarks>
    internal const string LegacySessionCookiePath = RoutePrefix;

    public static IEndpointRouteBuilder MapHostedChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder chat = endpoints
            .MapGroup(RoutePrefix)
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
        IClientNetworkKeys networkKeys,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);

        StartedWebChatSession? started = await handler
            .StartAsync(
                new StartWebChatConversationCommand(
                    publicKey,
                    ClientNetworkIdentity.Resolve(http, networkKeys)),
                cancellationToken)
            .ConfigureAwait(false);

        if (started is null)
        {
            return Results.NotFound();
        }

        Guid conversationId = started.Conversation.ConversationId;

        // Expire whatever an earlier release left at the broader path, so a
        // browser upgrading across this change never sends two cookies of the same
        // name on one request.
        //
        // <strong>Before the append, and the order is not cosmetic.</strong>
        // Deleting a cookie removes any pending Set-Cookie for the same name from
        // this response, matching the path as a substring -- and every
        // per-conversation path begins with the legacy one, so doing this second
        // would silently strip the credential that was just issued and hand the
        // visitor a conversation they could never reach. Found by the cookie test
        // failing, which is why it is written down here rather than left as an
        // arrangement that happens to work.
        http.Response.Cookies.Delete(
            SessionCookieName,
            new CookieOptions { Path = LegacySessionCookiePath });

        // The one moment the raw credential exists outside the visitor's browser.
        // It goes into a cookie and nowhere else -- not into the body below, which
        // has no field for it.
        http.Response.Cookies.Append(
            SessionCookieName,
            started.AccessToken,
            SessionCookieOptions(environment, ConversationCookiePath(conversationId)));

        // The credential must never be cached by anything between here and the
        // browser.
        http.Response.Headers.CacheControl = "no-store";

        return Results.Ok(
            new StartedConversationResponse(conversationId, started.Conversation.AgentName));
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
                    ClientNetworkIdentity.Resolve(http, networkKeys)),
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
    /// The path one conversation's credential is scoped to.
    /// </summary>
    /// <remarks>
    /// Exactly the prefix of that conversation's own two routes — reading its
    /// transcript and sending into it. Narrower than the group: this cookie is not
    /// sent when the page reads the endpoint's public face, not sent when a
    /// conversation is started, and not sent to any other conversation's routes. A
    /// credential that never travels where it is not needed cannot be captured
    /// there either.
    /// <para>
    /// The id is formatted invariantly and without braces or dashes so the path
    /// matches the route the router produced, whatever the host's culture.
    /// </para>
    /// </remarks>
    internal static string ConversationCookiePath(Guid conversationId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{RoutePrefix}/conversations/{conversationId:D}");

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
    /// <para>
    /// <strong>The cookie's lifetime is not the session's.</strong> The browser
    /// forgets this when it closes; the server independently refuses it once the
    /// stored session expiry has passed. Two bounds rather than one, because a
    /// cookie's lifetime is entirely under the holder's control — a copied cookie
    /// value has no browser session to end — and only the server-side one is a
    /// guarantee.
    /// </para>
    /// </remarks>
    private static CookieOptions SessionCookieOptions(IHostEnvironment environment, string path) =>
        new()
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = path,
            IsEssential = true,
        };
}
