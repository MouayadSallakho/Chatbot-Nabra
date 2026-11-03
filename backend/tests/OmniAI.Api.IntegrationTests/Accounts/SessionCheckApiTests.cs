using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// Phase 37.5b, row 29: <c>GET /api/auth/session</c>. Always 200 for a good
/// request, never a cookie, never cached, rate-limited per network; and the same
/// answer about the signed-in person as <c>/api/auth/me</c>, which is unchanged.
/// </summary>
public sealed class SessionCheckApiTests(AccountApiFixture api) : IClassFixture<AccountApiFixture>
{
    private const string NewPassword = "a-brand-new-passphrase-2026";

    [Fact]
    public async Task Signed_out_it_answers_false_without_a_cookie_and_me_still_answers_401()
    {
        using HttpClient visitor = api.CreateClient();

        using HttpResponseMessage session = await visitor.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal("""{"authenticated":false}""", await session.Content.ReadAsStringAsync());
        AssertNoCookieAndNoStore(session);

        using HttpResponseMessage me = await visitor.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Signed_in_it_answers_with_exactly_what_me_answers()
    {
        using HttpClient member = api.CreateClient();
        await ApiAuthentication.RegisterAsync(member);

        using HttpResponseMessage session = await member.GetAsync("/api/auth/session");
        using HttpResponseMessage me = await member.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        AssertNoCookieAndNoStore(session);

        using JsonDocument sessionBody = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        using JsonDocument meBody = JsonDocument.Parse(await me.Content.ReadAsStringAsync());

        Assert.True(sessionBody.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(meBody.RootElement.GetRawText(), sessionBody.RootElement.GetProperty("user").GetRawText());
        Assert.Equal(["authenticated", "user"], sessionBody.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task A_revoked_session_answers_false_and_the_check_does_not_sign_it_out()
    {
        string email = ApiAuthentication.UniqueEmail();
        using HttpClient member = api.CreateClient();
        Guid userId = await ApiAuthentication.RegisterAsync(member, email);

        using HttpClient visitor = api.CreateClient();
        (await AccountApiFixture.ForgotAsync(visitor, email)).Dispose();
        MimeMessage message = await api.WaitForEmailAsync(email);
        string token = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(message, "/reset-password"), "token");
        using (HttpResponseMessage reset = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, NewPassword))
        {
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        }

        using HttpResponseMessage session = await member.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal("""{"authenticated":false}""", await session.Content.ReadAsStringAsync());
        AssertNoCookieAndNoStore(session);

        // The ordinary routes behave exactly as before: 401, and the cookie is cleared.
        using HttpResponseMessage me = await member.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.True(me.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_session_whose_account_is_gone_answers_false()
    {
        using HttpClient member = api.CreateClient();
        Guid userId = await ApiAuthentication.RegisterAsync(member);

        await using (OmniAiDbContext context = api.CreateDbContext())
        {
            await context.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
        }

        using HttpResponseMessage session = await member.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal("""{"authenticated":false}""", await session.Content.ReadAsStringAsync());
        AssertNoCookieAndNoStore(session);
    }

    internal static void AssertNoCookieAndNoStore(HttpResponseMessage response)
    {
        Assert.False(response.Headers.Contains("Set-Cookie"), "The session check wrote a cookie.");
        Assert.True(response.Headers.CacheControl?.NoStore, "The session check must send Cache-Control: no-store.");
    }
}

/// <summary>A host whose session cookie is past half its life within seconds.</summary>
public sealed class ShortSessionFixture : AccountApiFixture
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.PostConfigure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            options => options.ExpireTimeSpan = Lifetime);
    }
}

/// <summary>
/// The sliding expiration reissues a cookie past half its life on an ordinary
/// request, and never on the session check.
/// </summary>
public sealed class SessionCheckRenewalApiTests(ShortSessionFixture api) : IClassFixture<ShortSessionFixture>
{
    [Fact]
    public async Task The_check_never_extends_a_session_the_sliding_expiration_would()
    {
        using HttpClient member = api.CreateClient();
        await ApiAuthentication.RegisterAsync(member);

        await Task.Delay(ShortSessionFixture.Lifetime * 0.6);

        using HttpResponseMessage session = await member.GetAsync("/api/auth/session");

        Assert.Contains("\"authenticated\":true", await session.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        SessionCheckApiTests.AssertNoCookieAndNoStore(session);

        // The control: the same cookie on an ordinary route is reissued.
        using HttpResponseMessage me = await member.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(me.Headers.Contains("Set-Cookie"), "The control route should have renewed the cookie.");
    }
}

/// <summary>A host whose per-request stamp check reissues the cookie on every request.</summary>
public sealed class EagerStampRenewalFixture : AccountApiFixture
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
    }
}

/// <summary>The stamp check's renewal is skipped on the session check too.</summary>
public sealed class SessionCheckStampRenewalApiTests(EagerStampRenewalFixture api) : IClassFixture<EagerStampRenewalFixture>
{
    [Fact]
    public async Task The_stamp_check_never_reissues_the_cookie_on_the_session_check()
    {
        using HttpClient member = api.CreateClient();
        await ApiAuthentication.RegisterAsync(member);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        using HttpResponseMessage session = await member.GetAsync("/api/auth/session");

        Assert.Contains("\"authenticated\":true", await session.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        SessionCheckApiTests.AssertNoCookieAndNoStore(session);

        using HttpResponseMessage me = await member.GetAsync("/api/auth/me");
        Assert.True(me.Headers.Contains("Set-Cookie"), "The control route should have renewed the cookie.");
    }
}

/// <summary>A tiny session-check limit, so it can be seen to fire.</summary>
public sealed class TightSessionCheckFixture : AccountApiFixture
{
    protected override IReadOnlyDictionary<string, string> Limits =>
        new Dictionary<string, string>(base.Limits, StringComparer.Ordinal)
        {
            ["AccountProtection:SessionChecksPerNetwork:Limit"] = "3",
        };
}

/// <summary>The session check is limited per network, for every caller.</summary>
public sealed class SessionCheckLimitApiTests(TightSessionCheckFixture api) : IClassFixture<TightSessionCheckFixture>
{
    [Fact]
    public async Task The_fourth_check_from_a_network_is_refused_and_another_network_is_not()
    {
        using HttpClient onA = await api.CreateVisitorAsync(IPAddress.Parse("198.51.100.201"));

        List<HttpStatusCode> answers = [];

        for (int attempt = 0; attempt < 4; attempt++)
        {
            using HttpResponseMessage answer = await onA.GetAsync("/api/auth/session");
            answers.Add(answer.StatusCode);

            if (answer.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.Equal("auth.throttled", await AccountApiFixture.CodeOf(answer));
                Assert.True(answer.Headers.Contains("Retry-After"));
                SessionCheckApiTests.AssertNoCookieAndNoStore(answer);
            }
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], answers);

        using HttpClient onB = await api.CreateVisitorAsync(IPAddress.Parse("198.51.100.202"));
        using HttpResponseMessage elsewhere = await onB.GetAsync("/api/auth/session");
        Assert.Equal(HttpStatusCode.OK, elsewhere.StatusCode);
    }
}

/// <summary>Fail closed: without its limiter the check is refused, not served unlimited.</summary>
public sealed class SessionCheckUnprotectedApiTests(UnreachableAccountProtectionFixture api)
    : IClassFixture<UnreachableAccountProtectionFixture>
{
    [Fact]
    public async Task An_unreachable_limiter_answers_503_with_its_code()
    {
        using HttpClient visitor = api.CreateClient();

        using HttpResponseMessage refused = await visitor.GetAsync("/api/auth/session");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("auth.throttle.unavailable", await AccountApiFixture.CodeOf(refused));
        SessionCheckApiTests.AssertNoCookieAndNoStore(refused);
    }
}
