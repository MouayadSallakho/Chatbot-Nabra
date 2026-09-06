using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using OmniAI.Api.Security;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Identity;

namespace OmniAI.Api.Auth;

/// <summary>
/// Registration, sign-in, sign-out, the current account, and the antiforgery
/// bootstrap.
/// </summary>
/// <remarks>
/// <strong>Cookies, not tokens.</strong> The admin application is a same-origin
/// browser client, and for that shape a session cookie is both simpler and safer
/// than a bearer token: the browser stores it where script cannot reach it,
/// attaches it automatically, and drops it on expiry. A JWT would have to live
/// somewhere JavaScript can read -- which turns any XSS defect into account
/// takeover -- and would bring a refresh-token design, a revocation story and a
/// signing-key rotation problem, all to solve a cross-origin problem this client
/// does not have. External APIs, mobile clients and service-to-service calls may
/// well need tokens; they can have a design that fits them, when they exist.
/// <para>
/// <strong>What a cookie costs, and how it is paid.</strong> Cookies ride along
/// with cross-site requests, so a cookie-authenticated API needs antiforgery
/// protection. Every state-changing endpoint in this file, and in the tenant and
/// agent groups, is behind <see cref="AntiforgeryEndpointFilter"/>.
/// </para>
/// </remarks>
internal static class AuthEndpoints
{
    /// <summary>
    /// The single sentence a failed sign-in ever produces.
    /// </summary>
    /// <remarks>
    /// <strong>Deliberately uninformative, and it covers three different
    /// outcomes.</strong> No account with that address, an account with a
    /// different password, and an account currently locked out all produce this
    /// exact response with this exact status. Distinguishing the first from the
    /// second turns the login form into an account-existence oracle -- somebody
    /// can learn which of a list of email addresses are registered here simply by
    /// trying them.
    /// <para>
    /// Folding lockout in with them is a genuine trade, and it is made
    /// deliberately: telling a caller "this account is locked" is equally an
    /// admission that the account exists. The cost is real -- somebody who has
    /// locked themselves out is told only that their credentials are wrong, and
    /// will keep trying -- and it is bounded by the fifteen-minute window, after
    /// which the same credentials simply work. Telling the account holder over
    /// email is the right fix and arrives with email transport.
    /// </para>
    /// </remarks>
    private const string SignInFailed = "The email address or password is incorrect.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder auth = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication")
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        // Anonymous by necessity: somebody who is not signed in has to be able to
        // obtain a token, create an account and sign in. These three are the
        // complete anonymous surface of the authenticated API, alongside /health
        // and the development OpenAPI document.
        auth.MapGet("/csrf", IssueAntiforgeryTokenAsync)
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken");

        auth.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .WithName("Register");

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithName("Login");

        auth.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout");

        auth.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser");

        return endpoints;
    }

    /// <summary>
    /// <c>GET /api/auth/csrf</c>. Establishes the antiforgery token pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two values are produced. The secret half is written by the framework into
    /// its own <c>HttpOnly</c> cookie; the request half is written here into a
    /// cookie the browser's script can read, so the client can echo it in a
    /// header. A request is accepted only when both are present and match, which
    /// a cross-site attacker cannot arrange: they can cause the browser to send
    /// cookies, but the same-origin policy stops them reading one to build the
    /// header.
    /// </para>
    /// <para>
    /// <strong>It exposes no credential.</strong> The request token authenticates
    /// nobody on its own and grants nothing; it only proves that whoever composed
    /// the request could read this origin's cookies.
    /// </para>
    /// <para>
    /// <strong>Must be called again after the signed-in identity changes.</strong>
    /// Antiforgery tokens are bound to the authenticated user, so one obtained
    /// while anonymous stops validating the moment somebody signs in. Rather than
    /// leave that to the client to remember, register, login and logout each
    /// refresh the cookie themselves before returning.
    /// </para>
    /// <para>
    /// <c>204</c>, with the value in a cookie rather than in a body. A body would
    /// invite a client to store the token in JavaScript memory and attach it by
    /// hand, which is more code doing what the browser already does correctly.
    /// </para>
    /// </remarks>
    private static Task<IResult> IssueAntiforgeryTokenAsync(
        HttpContext http,
        IAntiforgery antiforgery,
        IHostEnvironment environment)
    {
        IssueAntiforgeryCookie(http, antiforgery, environment);

        return Task.FromResult(Results.NoContent());
    }

    /// <summary>
    /// <c>POST /api/auth/register</c>. Creates an account and signs it in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Signing the new account in immediately is a deliberate
    /// decision.</strong> Somebody who has just proved they hold the password by
    /// choosing it has told us everything a sign-in would; making them retype it
    /// adds a step and no security. It is only defensible because email
    /// confirmation is not required in this phase -- once it is, registration must
    /// stop granting a session and start granting a pending one.
    /// </para>
    /// <para>
    /// <strong>No tenant is created.</strong> A workspace is a separate,
    /// deliberate act with a name the person chooses. Inventing one from the email
    /// domain would mint a slug nobody asked for, and would silently make two
    /// colleagues who register from the same company either collide or end up in
    /// separate workspaces depending on which happened first.
    /// </para>
    /// <para>
    /// <strong>A taken address is reported plainly.</strong> That is an account
    /// enumeration vector, and it is an accepted, documented one: a self-service
    /// registration form cannot both refuse duplicates and hide that it did so
    /// without email confirmation to defer the answer into the inbox. Closing it
    /// is part of the deferred email-verification work.
    /// </para>
    /// </remarks>
    private static async Task<IResult> RegisterAsync(
        HttpContext http,
        RegisterRequest request,
        UserManager<OmniAiUser> users,
        SignInManager<OmniAiUser> signIn,
        IAntiforgery antiforgery,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(request);

        string email = (request.Email ?? string.Empty).Trim();
        string password = request.Password ?? string.Empty;

        if (email.Length == 0 || password.Length == 0)
        {
            return InvalidRegistration("An email address and a password are both required.");
        }

        // The id is allocated here, as a UUIDv7, rather than left to Identity's
        // default Guid.NewGuid(). Every identifier in this platform is version 7,
        // and a randomly ordered primary key on the one table that will grow with
        // the customer base is the one place the difference actually shows up in
        // index behaviour.
        OmniAiUser user = new()
        {
            Id = UserId.New().Value,
            UserName = email,
            Email = email,
        };

        IdentityResult created = await users.CreateAsync(user, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            // Identity's own messages describe the rule that was broken -- how
            // long a password must be, that an address is taken, that an address
            // is malformed. They are written for the person filling in the form
            // and contain no credential, no hash and no internal detail.
            return InvalidRegistration(string.Join(" ", created.Errors.Select(error => error.Description)));
        }

        await signIn.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        // The identity changed, so the token obtained while anonymous no longer
        // validates. Reissued here rather than left for the client to discover
        // through a rejected write.
        IssueAntiforgeryCookie(http, antiforgery, environment);

        // 201 without a Location header: an account was created, but this API
        // publishes no per-account resource to point at. User administration is a
        // later phase, and inventing a URL now that nothing serves would be a
        // promise the API cannot keep.
        return Results.Created((string?)null, Describe(user));
    }

    /// <summary>
    /// <c>POST /api/auth/login</c>. Verifies a password and issues the session cookie.
    /// </summary>
    private static async Task<IResult> LoginAsync(
        HttpContext http,
        LoginRequest request,
        UserManager<OmniAiUser> users,
        SignInManager<OmniAiUser> signIn,
        IAntiforgery antiforgery,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(request);

        string email = (request.Email ?? string.Empty).Trim();
        string password = request.Password ?? string.Empty;

        OmniAiUser? user = email.Length == 0
            ? null
            : await users.FindByEmailAsync(email).ConfigureAwait(false);

        if (user is null)
        {
            // Hash the supplied password against a throwaway account before
            // answering. Without this, an unknown address returns in microseconds
            // while a known one pays for a deliberately slow key derivation, and
            // that difference is measurable over a network -- which would restore
            // the account-existence oracle the shared message exists to close.
            // Identity's own hasher is used; nothing bespoke is invented, and the
            // result is discarded.
            users.PasswordHasher.HashPassword(new OmniAiUser(), password);

            return SignInRejected();
        }

        // lockoutOnFailure: the whole point of the configured policy. Without it
        // the failure counter is never incremented and the lockout window is
        // unreachable.
        SignInResult result = await signIn
            .PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Every unsuccessful outcome -- wrong password, locked out, not
            // allowed, two-factor required -- answers identically. See the
            // SignInFailed note.
            return SignInRejected();
        }

        IssueAntiforgeryCookie(http, antiforgery, environment);

        return Results.Ok(Describe(user));
    }

    /// <summary>
    /// <c>POST /api/auth/logout</c>. Ends the session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SignOutAsync</c> instructs the browser to delete the session cookie, and
    /// the ticket inside it is not accepted afterwards because the cookie is gone.
    /// A server-side revocation list would additionally invalidate a cookie that
    /// had been copied off the machine before sign-out -- that is a real gap, it
    /// belongs with security-event auditing, and it is deferred rather than
    /// half-built here.
    /// </para>
    /// <para>
    /// A fresh anonymous antiforgery token is issued on the way out, so the login
    /// form the client returns to can post immediately instead of failing once and
    /// then bootstrapping.
    /// </para>
    /// <para>
    /// It requires authentication and a valid token. Both matter: an endpoint that
    /// any site could trigger would let a third party sign people out of this
    /// application at will, which is a denial of service rather than a courtesy.
    /// </para>
    /// </remarks>
    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        SignInManager<OmniAiUser> signIn,
        IAntiforgery antiforgery,
        IHostEnvironment environment)
    {
        await signIn.SignOutAsync().ConfigureAwait(false);

        IssueAntiforgeryCookie(http, antiforgery, environment);

        return Results.NoContent();
    }

    /// <summary>
    /// <c>GET /api/auth/me</c>. The signed-in account.
    /// </summary>
    /// <remarks>
    /// The client's way of asking "is this cookie still good, and whose is it" on
    /// startup. Anonymous callers never reach the body: authorization answers
    /// <c>401</c> first, which is what tells a freshly loaded application to show
    /// the sign-in screen.
    /// <para>
    /// A <c>401</c> is also possible for a caller who <em>was</em> authenticated:
    /// a cookie whose account has since been deleted resolves to nothing here.
    /// Answered the same way, because it means the same thing to the client.
    /// </para>
    /// </remarks>
    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext http,
        UserManager<OmniAiUser> users)
    {
        OmniAiUser? user = await users.GetUserAsync(http.User).ConfigureAwait(false);

        if (user is null)
        {
            return Results.Problem(
                title: "Not authenticated",
                detail: "This request does not identify a signed-in user.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Ok(Describe(user));
    }

    /// <summary>Projects an account onto the two fields this API publishes.</summary>
    private static CurrentUserResponse Describe(OmniAiUser user) =>
        new(user.Id, user.Email ?? string.Empty);

    /// <summary>Writes the readable request-token cookie.</summary>
    private static void IssueAntiforgeryCookie(
        HttpContext http,
        IAntiforgery antiforgery,
        IHostEnvironment environment)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(http);

        if (tokens.RequestToken is null)
        {
            return;
        }

        http.Response.Cookies.Append(
            AuthenticationSetup.XsrfCookieName,
            tokens.RequestToken,
            AuthenticationSetup.XsrfCookieOptions(environment));
    }

    private static IResult InvalidRegistration(string detail) =>
        Results.Problem(
            title: "Registration rejected",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult SignInRejected() =>
        Results.Problem(
            title: "Sign-in failed",
            detail: SignInFailed,
            statusCode: StatusCodes.Status401Unauthorized);
}
