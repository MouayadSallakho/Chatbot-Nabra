using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using OmniAI.Application.Security;
using OmniAI.Infrastructure.Identity;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.Security;

/// <summary>
/// Registers authentication, the antiforgery service and the current-user
/// adapter.
/// </summary>
/// <remarks>
/// Kept apart from the rest of the composition root because it is the security
/// configuration of the whole platform, and a reviewer should be able to read all
/// of it in one place rather than find it interleaved with handler registrations.
/// </remarks>
internal static class AuthenticationSetup
{
    /// <summary>The name of the sign-in cookie.</summary>
    /// <remarks>
    /// Not the framework default, which announces which framework is in use for
    /// no benefit. Deliberately <em>not</em> given the <c>__Host-</c> prefix: that
    /// prefix requires the <c>Secure</c> attribute, which local HTTP development
    /// cannot set, so the cookie name would have to differ between environments --
    /// and a cookie whose name changes with configuration is exactly the sort of
    /// difference that works everywhere except production. Adopting the prefix is
    /// part of the deferred production-hardening work, alongside verifying HTTPS
    /// termination at the reverse proxy.
    /// </remarks>
    private const string SessionCookieName = "omniai.session";

    /// <summary>The cookie Angular reads the antiforgery request token from.</summary>
    /// <remarks>
    /// This name and the header below are Angular's <c>HttpClient</c> convention.
    /// They are stated explicitly on both sides rather than relied on as defaults,
    /// so a change to either is a visible edit in two files instead of a silent
    /// mismatch that only shows up as rejected writes.
    /// </remarks>
    public const string XsrfCookieName = "XSRF-TOKEN";

    /// <summary>The header Angular echoes the antiforgery request token in.</summary>
    public const string XsrfHeaderName = "X-XSRF-TOKEN";

    /// <summary>
    /// How long a sign-in remains valid without activity.
    /// </summary>
    /// <remarks>
    /// Eight hours, sliding: long enough to cover a working day without
    /// re-authentication, short enough that an unattended browser does not stay
    /// signed in indefinitely. There is no "remember me", so the cookie is a
    /// session cookie as well and does not survive the browser closing.
    /// </remarks>
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static IServiceCollection AddOmniAiAuthentication(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        // Development is the only environment allowed to serve the cookie over
        // plain HTTP, and it is named explicitly rather than inferred. Everything
        // that is not Development -- Staging, Production, an environment nobody
        // has invented yet -- requires Secure. The default is therefore the safe
        // one: a deployment that forgets to set ASPNETCORE_ENVIRONMENT gets the
        // strict behaviour, not the relaxed one.
        bool allowInsecureCookies = environment.IsDevelopment();

        CookieSecurePolicy securePolicy = allowInsecureCookies
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        services.AddIdentityCore<OmniAiUser>(ConfigureIdentity)
            .AddEntityFrameworkStores<OmniAiDbContext>()
            // SignInManager is what turns a verified password into a cookie. It is
            // not part of AddIdentityCore's default set, because a headless API
            // that only validates credentials does not need it.
            .AddSignInManager();

        // AddIdentity was rejected in favour of AddIdentityCore: the former
        // requires a role type, which would have brought three role tables and a
        // role manager into a platform that has no roles.
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        // The application cookie, configured after AddIdentityCookies so these
        // settings win.
        //
        // All four Identity cookie schemes are registered above and only this one
        // is ever issued. The external and two-factor schemes exist because
        // SignInManager.SignOutAsync signs out of them by name and would throw if
        // they were absent; no flow in this platform ever sets them, so no
        // additional cookie reaches a browser.
        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            options =>
            {
                options.Cookie.Name = SessionCookieName;

                // The browser must never be able to read this value. It is the
                // whole session; script that can read it can impersonate the
                // person holding it, and an XSS defect anywhere on the page would
                // become account takeover rather than a bug.
                options.Cookie.HttpOnly = true;

                options.Cookie.SecurePolicy = securePolicy;
                options.Cookie.Path = "/";

                // Lax rather than Strict. The admin application is same-origin, so
                // every request that needs this cookie is a same-site one and
                // Strict would cost nothing in security terms -- but it would mean
                // that following a link into the admin from anywhere else showed a
                // signed-out application until the reader navigated again, which
                // reads as a broken session. Lax withholds the cookie from
                // cross-site POSTs, which is the case that matters, and the
                // antiforgery token covers those regardless.
                options.Cookie.SameSite = SameSiteMode.Lax;

                options.ExpireTimeSpan = SessionLifetime;
                options.SlidingExpiration = true;

                // This is an API. The framework default is a 302 to a login page
                // that does not exist here, which would reach the Angular client
                // as a successful navigation to HTML and be reported as a JSON
                // parse failure -- burying an expired session under an unrelated
                // error. Both events answer with a status instead, and the empty
                // body is filled in as Problem Details by UseStatusCodePages.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;

                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();

        // Antiforgery. The header is where the request token is expected; the
        // cookie holding the *other* half stays HttpOnly and framework-named,
        // because it is a secret.
        //
        // SecurePolicy is the same policy the session cookie uses, and getting
        // here took a correction worth recording.
        //
        // ASP.NET Core's antiforgery service throws when this policy is Always and
        // Request.IsHttps is false. Behind a TLS-terminating proxy that condition
        // is met on every request, so the first attempt at this phase relaxed the
        // policy to SameAsRequest to stop the exception. That was the wrong fix:
        // it removed the guarantee rather than the fault, and it would have let a
        // browser send these cookies over plain HTTP in production.
        //
        // The scheme is now carried across the proxy hop by ForwardedHeadersSetup,
        // so Request.IsHttps is true in a correctly configured deployment and
        // Always both holds and works. Where it is NOT true outside Development,
        // SecureTransportMiddleware refuses the request before anything can issue
        // a cookie -- the antiforgery exception is no longer reachable, and the
        // failure mode is a loud refusal rather than a quiet downgrade.
        services.AddAntiforgery(options =>
        {
            options.HeaderName = XsrfHeaderName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = securePolicy;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        // Required by HttpCurrentUser: it needs the ambient request to read the
        // principal from, and there is no other way for a scoped service that is
        // not itself part of the pipeline to reach it.
        services.AddHttpContextAccessor();

        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }

    /// <summary>
    /// Cookie options for the readable antiforgery cookie.
    /// </summary>
    /// <remarks>
    /// <strong>Deliberately readable by script, and that is not a mistake.</strong>
    /// The double-submit pattern requires the client to copy this value into a
    /// header, which it cannot do if the cookie is <c>HttpOnly</c>. It is safe
    /// because it is not a credential: on its own it authenticates nothing, and it
    /// is only accepted alongside the secret half, which stays <c>HttpOnly</c>.
    /// What it prevents is a third-party site causing the browser to send a
    /// state-changing request with the session cookie attached -- that site can
    /// make the browser send the cookie, but cannot read this one to build the
    /// matching header.
    /// </remarks>
    public static CookieOptions XsrfCookieOptions(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // Secure outside Development, exactly like the two framework cookies it is
        // paired with. Being readable by script and being sent in the clear are
        // unrelated properties: this half must be readable so the client can echo
        // it, and that is no reason to let it travel over plain HTTP.
        //
        // A bool rather than a policy, because CookieOptions has no SameAsRequest.
        // It is keyed on IsDevelopment() so it matches CookieSecurePolicy.Always
        // everywhere else -- if the two disagreed, the browser would drop one half
        // of the pair and keep the other, and every write would fail with a
        // mismatched token rather than a missing one.
        return new CookieOptions
        {
            HttpOnly = false,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
        };
    }

    /// <summary>
    /// The password and lockout policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Length over composition.</strong> Twelve characters, and no
    /// requirement for a digit, a symbol or mixed case. Composition rules are the
    /// reason people produce <c>Password1!</c> -- they push everybody towards the
    /// same small set of predictable substitutions while adding very little real
    /// entropy, and current guidance from NIST SP 800-63B is explicit that
    /// arbitrary composition rules should not be imposed. A twelve-character
    /// passphrase is both stronger and easier to remember than an eight-character
    /// one wearing punctuation.
    /// </para>
    /// <para>
    /// <strong>Lockout: five failures, fifteen minutes.</strong> Enough to stop an
    /// online guessing attack from getting anywhere, short enough that somebody
    /// who genuinely mistyped is not locked out for the afternoon. It applies to
    /// new accounts as well as established ones -- the alternative leaves every
    /// freshly registered account unprotected, which is precisely the set an
    /// attacker would target. Identity's own counter is used; no bespoke
    /// brute-force tracking exists, and no distributed rate limiter is introduced
    /// for a scaling problem this deployment does not have.
    /// </para>
    /// <para>
    /// <strong>Email confirmation is off, and that is a deferral, not an
    /// oversight.</strong> Requiring a confirmed account with no mail transport
    /// configured would mean nobody could ever sign in. Email verification, and
    /// the account-enumeration hardening at registration that comes with it, are
    /// explicitly a later phase.
    /// </para>
    /// </remarks>
    private static void ConfigureIdentity(IdentityOptions options)
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        // Email is the login identifier, so it has to be unique. The database
        // enforces it too -- see the unique index on normalized_email -- because
        // this option is checked by a read that two concurrent registrations can
        // both pass.
        options.User.RequireUniqueEmail = true;

        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedPhoneNumber = false;
    }
}
