using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using OmniAI.Api.Security;
using OmniAI.Application.Accounts;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Security;
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
        // obtain a token, create an account and sign in -- and, from Phase 37.3,
        // ask for and use a password reset link. These five are the complete
        // anonymous surface of the authenticated API, alongside /health and the
        // development OpenAPI document.
        auth.MapGet("/csrf", IssueAntiforgeryTokenAsync)
            .AllowAnonymous()
            .WithName("GetAntiforgeryToken");

        auth.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .WithName("Register")
            .WithSummary("Creates an account and signs it in. Optional language (\"en\" or \"ar\"), else Accept-Language; every refusal carries a code.")
            .Produces<CurrentUserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Signs in. Rate-limited per address and per network (429 auth.throttled, 503 auth.throttle.unavailable); every refusal carries a code.")
            .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Phase 37.3. See PasswordResetEndpoints.
        auth.MapPost("/password/forgot", PasswordResetEndpoints.ForgotAsync)
            .AllowAnonymous()
            .WithName("ForgotPassword")
            .WithSummary("Asks for a password reset email. Always 202 with no body, whether or not the address has an account.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        auth.MapPost("/password/reset", PasswordResetEndpoints.ResetAsync)
            .AllowAnonymous()
            .WithName("ResetPassword")
            .WithSummary("Sets a new password with a reset link's user id and token, and ends every session of that account.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        auth.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout");

        auth.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in account: id, email, and the stored language (\"en\", \"ar\" or null).")
            .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

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

        if (!AccountProblems.TryReadLanguage(request.Language, out UserLanguage? requested, out IResult? unsupported))
        {
            return unsupported!;
        }

        string email = (request.Email ?? string.Empty).Trim();
        string password = request.Password ?? string.Empty;

        if (email.Length == 0 || password.Length == 0)
        {
            return InvalidRegistration(
                "An email address and a password are both required.",
                AccountProblems.RegistrationIncomplete);
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

            // Phase 37.3: the language the person is using, else their browser's,
            // else none (which reads English).
            Language = UserLanguages.Code(AccountProblems.Preferred(http, requested)),
        };

        IdentityResult created = await users.CreateAsync(user, password).ConfigureAwait(false);

        if (!created.Succeeded)
        {
            // Identity's own messages describe the rule that was broken -- how
            // long a password must be, that an address is taken, that an address
            // is malformed. They are written for the person filling in the form
            // and contain no credential, no hash and no internal detail. Each now
            // also carries a code (Phase 37.3, row 6b).
            return AccountProblems.FromIdentity("Registration rejected", created.Errors);
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
    /// <remarks>
    /// <para>
    /// <strong>Failures are limited, attempts are not (Phase 37.3 Revision 1, D-2: no
    /// hard lockout).</strong> Three failure counters in the same Redis on every
    /// instance: one address on one network, one network, and one address on every
    /// network. They are checked before the password is, and a refusal (429) comes
    /// before the check, so throttling says nothing about the account or the
    /// password. The check also reserves an in-flight slot atomically, so a burst of
    /// parallel guesses cannot overshoot a limit before the first is counted. A failed check, an unknown address included, increments all
    /// three; a successful sign-in increments nothing and clears that address's
    /// failures on that network. A whole support team behind one address signs in
    /// freely, and a stranger's guesses slow only the stranger's network. An
    /// unreachable limiter refuses with 503 rather than letting guesses through.
    /// </para>
    /// <para>
    /// <strong>Identity's lockout is not applied here</strong> (<c>lockoutOnFailure:
    /// false</c>), so nobody can lock another person out by knowing their address.
    /// </para>
    /// <para>
    /// <strong>The language is filled only while the account has none.</strong>
    /// From an optional <c>language</c>, else <c>Accept-Language</c>; a chosen one is
    /// never overwritten here.
    /// </para>
    /// </remarks>
    private static async Task<IResult> LoginAsync(
        HttpContext http,
        LoginRequest request,
        UserManager<OmniAiUser> users,
        SignInManager<OmniAiUser> signIn,
        IAntiforgery antiforgery,
        IHostEnvironment environment,
        IAccountProtection protection,
        IAccountSubjectKeys subjects,
        IClientNetworkKeys networks,
        IAccountLanguages languages,
        ISecurityEventLog audit,
        TimeProvider clock,
        ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(protection);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggers);

        if (!AccountProblems.TryReadLanguage(request.Language, out UserLanguage? requested, out IResult? unsupported))
        {
            return unsupported!;
        }

        string email = (request.Email ?? string.Empty).Trim();
        string password = request.Password ?? string.Empty;

        string subject;
        ClientNetworkKey? client;
        AccountProtectionDecision decision;

        try
        {
            subject = subjects.Derive(users.NormalizeEmail(email) ?? string.Empty);
            client = ClientNetworkIdentity.Resolve(http, networks);
            decision = await protection
                .CheckSignInAsync(subject, client, http.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (Exception unprotected) when (unprotected is AccountProtectionUnavailableException or PublicChatProtectionUnavailableException)
        {
            return AccountProblems.Unavailable(loggers.CreateLogger(typeof(AuthEndpoints)), unprotected);
        }

        if (!decision.Allowed)
        {
            await audit
                .RecordAsync(
                    SecurityEvent.Record(
                        SecurityEventKinds.SignInThrottled,
                        clock.GetUtcNow(),
                        subjectKey: subject,
                        clientNetworkKey: client?.Value),
                    http.RequestAborted)
                .ConfigureAwait(false);

            return AccountProblems.TooMany(http, decision);
        }

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

            // Counted exactly like a wrong password, so the counters cannot tell
            // an unknown address from a known one either.
            return await FailedAsync(protection, subject, client, loggers, http.RequestAborted).ConfigureAwait(false);
        }

        // lockoutOnFailure: false (Revision 1). The failure limiter is the only
        // throttle; Identity's lockout would let anybody who knows an address lock
        // its owner out. A lockout left from before is still honoured until it
        // expires, and a password reset lifts it.
        SignInResult result = await signIn
            .PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: false)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            // Every unsuccessful outcome -- wrong password, an old lockout, not
            // allowed, two-factor required -- answers identically. See the
            // SignInFailed note.
            return await FailedAsync(protection, subject, client, loggers, http.RequestAborted).ConfigureAwait(false);
        }

        try
        {
            await protection.RecordSignInSuccessAsync(subject, client, CancellationToken.None).ConfigureAwait(false);
        }
        catch (AccountProtectionUnavailableException unrecorded)
        {
            // The person proved the password; an uncleared count only means their
            // earlier failures age out on their own.
            AccountProblems.LogNotCleared(loggers.CreateLogger(typeof(AuthEndpoints)), unrecorded);
        }

        if (AccountProblems.Preferred(http, requested) is UserLanguage language && user.Language is null)
        {
            await languages
                .FillIfUnsetAsync(UserId.From(user.Id), language, http.RequestAborted)
                .ConfigureAwait(false);

            user.Language = UserLanguages.Code(language);
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

    /// <summary>Projects an account onto the fields this API publishes.</summary>
    /// <remarks>Three from Phase 37.3: the stored language joined the id and the address.</remarks>
    private static CurrentUserResponse Describe(OmniAiUser user) =>
        new(user.Id, user.Email ?? string.Empty, UserLanguages.Code(UserLanguages.FromStored(user.Language)));

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

    /// <summary>
    /// Counts a failed sign-in, then refuses it. When the failure cannot be counted
    /// the answer is 503, as when the limiter cannot decide: fail closed.
    /// </summary>
    private static async Task<IResult> FailedAsync(
        IAccountProtection protection,
        string subject,
        ClientNetworkKey? client,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        try
        {
            await protection.RecordSignInFailureAsync(subject, client, cancellationToken).ConfigureAwait(false);
        }
        catch (AccountProtectionUnavailableException unprotected)
        {
            return AccountProblems.Unavailable(loggers.CreateLogger(typeof(AuthEndpoints)), unprotected);
        }

        return SignInRejected();
    }

    private static IResult InvalidRegistration(string detail, string code) =>
        AccountProblems.Problem(StatusCodes.Status400BadRequest, "Registration rejected", detail, code);

    /// <remarks>The same bytes for an unknown address and a wrong password, code included.</remarks>
    private static IResult SignInRejected() =>
        AccountProblems.Problem(
            StatusCodes.Status401Unauthorized,
            "Sign-in failed",
            SignInFailed,
            AccountProblems.CredentialsInvalid);
}
