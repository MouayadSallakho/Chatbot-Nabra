using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using OmniAI.Api.Security;
using OmniAI.Infrastructure.Identity;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// Password reset end to end (Phase 37.3): the request, the email, the link, the
/// reset, and everything that must and must not happen around them.
/// </summary>
public sealed class PasswordResetApiTests(AccountApiFixture api) : IClassFixture<AccountApiFixture>
{
    private const string NewPassword = "a-brand-new-passphrase-2026";

    [Fact]
    public async Task A_known_and_an_unknown_address_get_the_same_answer_and_only_the_known_one_an_email()
    {
        string known = ApiAuthentication.UniqueEmail();
        string unknown = ApiAuthentication.UniqueEmail();

        using (HttpClient owner = api.CreateClient())
        {
            await ApiAuthentication.RegisterAsync(owner, known);
        }

        using HttpClient visitor = api.CreateClient();

        using HttpResponseMessage forKnown = await AccountApiFixture.ForgotAsync(visitor, known);
        using HttpResponseMessage forUnknown = await AccountApiFixture.ForgotAsync(visitor, unknown);

        Assert.Equal(HttpStatusCode.Accepted, forKnown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, forUnknown.StatusCode);
        Assert.Equal(string.Empty, await forKnown.Content.ReadAsStringAsync());
        Assert.Equal(string.Empty, await forUnknown.Content.ReadAsStringAsync());
        Assert.Equal(Shape(forKnown), Shape(forUnknown));

        MimeMessage email = await api.WaitForEmailAsync(known);
        Assert.Equal("Nabra: Set a new password", email.Subject);

        await Task.Delay(1500);
        Assert.Equal(0, api.EmailsTo(unknown));

        // Both requests are audited, by the keyed hash of the address; only the
        // known one names an account.
        await using OmniAiDbContext context = api.CreateDbContext();
        List<(Guid? User, string? Subject)> requested = await RequestedEventsAsync(context);

        Assert.Contains(requested, row => row.User is not null);
        Assert.Contains(requested, row => row.User is null && row.Subject is { Length: > 0 });
    }

    [Fact]
    public async Task Repeated_requests_reuse_the_pending_email_rather_than_queue_another()
    {
        string email = ApiAuthentication.UniqueEmail();
        Guid userId;

        using (HttpClient owner = api.CreateClient())
        {
            userId = await ApiAuthentication.RegisterAsync(owner, email);
        }

        // Three requests in parallel, before the worker can send the first.
        using HttpClient visitor = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(visitor);

        HttpResponseMessage[] answers = await Task.WhenAll(
            Enumerable.Range(0, 3).Select(_ => visitor.PostAsJsonAsync("/api/auth/password/forgot", new { email })));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Accepted, answer.StatusCode));

        await using OmniAiDbContext context = api.CreateDbContext();

        int queued = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM account_emails WHERE user_id = {userId} AND kind = 'PasswordReset'")
            .SingleAsync();

        // The worker may have sent the first before the last arrived, so at most
        // one is pending at a time: never three.
        Assert.InRange(queued, 1, 2);
    }

    [Fact]
    public async Task The_emailed_link_sets_a_new_password_ends_every_session_and_works_only_once()
    {
        string email = ApiAuthentication.UniqueEmail();

        using HttpClient first = api.CreateClient();
        Guid userId = await ApiAuthentication.RegisterAsync(first, email);

        (HttpClient second, HttpResponseMessage signedIn) = await api.SignInAsync(email, ApiAuthentication.Password);
        using HttpClient secondSession = second;
        signedIn.Dispose();

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondSession.GetAsync("/api/auth/me")).StatusCode);

        using HttpClient visitor = api.CreateClient();
        (await AccountApiFixture.ForgotAsync(visitor, email)).Dispose();

        MimeMessage message = await api.WaitForEmailAsync(email);
        Uri link = AccountApiFixture.LinkTo(message, "/reset-password");

        Assert.Equal(userId.ToString("D"), AccountApiFixture.Fragment(link, "uid"));
        string token = AccountApiFixture.Fragment(link, "token");

        using HttpResponseMessage reset = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Every session that existed is over, on its very next request.
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secondSession.GetAsync("/api/auth/me")).StatusCode);

        // The new password signs in; the old one does not.
        (HttpClient fresh, HttpResponseMessage withNew) = await api.SignInAsync(email, NewPassword);
        using HttpClient freshSession = fresh;
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await freshSession.GetAsync("/api/auth/me")).StatusCode);

        (HttpClient stale, HttpResponseMessage withOld) = await api.SignInAsync(email, ApiAuthentication.Password);
        stale.Dispose();
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);

        // The link is single use: the stamp it carried is gone.
        using HttpResponseMessage again = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, "yet-another-passphrase-99");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("auth.reset.invalid", await AccountApiFixture.CodeOf(again));

        // "Your password was changed", without a link to act on.
        MimeMessage changed = await api.WaitForEmailAsync(email, skip: 1);
        Assert.Equal("Nabra: Your password was changed", changed.Subject);
        Assert.DoesNotContain("token=", changed.TextBody, StringComparison.Ordinal);
        Assert.Contains(AccountApiFixture.AppBase + "sign-in", changed.TextBody, StringComparison.Ordinal);

        await using OmniAiDbContext context = api.CreateDbContext();
        List<string> kinds = await KindsForAsync(context, userId);

        Assert.Contains("auth.password_reset.requested", kinds);
        Assert.Contains("auth.password_reset.completed", kinds);
        Assert.Contains("auth.sessions.revoked", kinds);
        Assert.Contains("auth.password_reset.rejected", kinds);
        await WaitForAsync(async () => (await KindsForAsync(context, userId)).Contains("auth.password_changed.email_sent"));
        Assert.Contains("auth.password_reset.email_sent", await KindsForAsync(context, userId));
    }

    [Fact]
    public async Task Every_bad_link_is_refused_with_the_same_answer()
    {
        string email = ApiAuthentication.UniqueEmail();
        Guid userId;

        using (HttpClient owner = api.CreateClient())
        {
            userId = await ApiAuthentication.RegisterAsync(owner, email);
        }

        using HttpClient visitor = api.CreateClient();

        HttpResponseMessage[] refusals =
        [
            await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), "not-a-token", NewPassword),
            await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), "CfDJ8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", NewPassword),
            await AccountApiFixture.ResetAsync(visitor, Guid.NewGuid().ToString("D"), "not-a-token", NewPassword),
            await AccountApiFixture.ResetAsync(visitor, "not-a-guid", "not-a-token", NewPassword),
            await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), null, NewPassword),
        ];

        List<string> bodies = [];

        foreach (HttpResponseMessage refusal in refusals)
        {
            Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
            bodies.Add(WithoutTrace(await refusal.Content.ReadAsStringAsync()));
            refusal.Dispose();
        }

        Assert.Single(bodies.Distinct(StringComparer.Ordinal));
        Assert.Contains("\"code\":\"auth.reset.invalid\"", bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_password_rule_is_reported_only_once_the_link_was_accepted()
    {
        string email = ApiAuthentication.UniqueEmail();

        using HttpClient owner = api.CreateClient();
        Guid userId = await ApiAuthentication.RegisterAsync(owner, email);

        using HttpClient visitor = api.CreateClient();
        (await AccountApiFixture.ForgotAsync(visitor, email)).Dispose();
        string token = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(await api.WaitForEmailAsync(email), "/reset-password"), "token");

        using HttpResponseMessage tooShort = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, "short");
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(await tooShort.Content.ReadAsStringAsync());
        Assert.Equal("auth.password.too_short", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains("auth.password.too_short", problem.RootElement.GetProperty("codes").EnumerateArray().Select(code => code.GetString()));

        // The link was not spent by the refusal: the same token still works.
        using HttpResponseMessage valid = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);

        // The old session ended with it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_token_expires_after_its_lifetime_and_belongs_to_one_account()
    {
        string email = ApiAuthentication.UniqueEmail();
        string other = ApiAuthentication.UniqueEmail();

        using (HttpClient owner = api.CreateClient())
        {
            await ApiAuthentication.RegisterAsync(owner, email);
            await ApiAuthentication.RegisterAsync(api.CreateClient(), other);
        }

        // The host's own key ring and Identity, with a clock this test moves.
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        UserManager<OmniAiUser> users = scope.ServiceProvider.GetRequiredService<UserManager<OmniAiUser>>();
        OmniAiUser account = (await users.FindByEmailAsync(email))!;
        OmniAiUser stranger = (await users.FindByEmailAsync(other))!;

        FixedTimeProvider clock = new(DateTimeOffset.UtcNow);
        PasswordResetTokenProvider provider = new(
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
            scope.ServiceProvider.GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>(),
            NullLogger<DataProtectorTokenProvider<OmniAiUser>>.Instance,
            clock);

        string token = await provider.GenerateAsync(UserManager<OmniAiUser>.ResetPasswordTokenPurpose, users, account);

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.True(await provider.ValidateAsync(UserManager<OmniAiUser>.ResetPasswordTokenPurpose, token, users, account));
        Assert.False(await provider.ValidateAsync(UserManager<OmniAiUser>.ResetPasswordTokenPurpose, token, users, stranger));
        Assert.False(await provider.ValidateAsync("EmailConfirmation", token, users, account));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(await provider.ValidateAsync(UserManager<OmniAiUser>.ResetPasswordTokenPurpose, token, users, account));
    }

    [Fact]
    public async Task Nothing_stored_or_logged_holds_the_link_or_the_token()
    {
        string email = ApiAuthentication.UniqueEmail();

        using HttpClient owner = api.CreateClient();
        Guid userId = await ApiAuthentication.RegisterAsync(owner, email);

        using HttpClient visitor = api.CreateClient();
        (await AccountApiFixture.ForgotAsync(visitor, email)).Dispose();

        Uri link = AccountApiFixture.LinkTo(await api.WaitForEmailAsync(email), "/reset-password");
        string token = AccountApiFixture.Fragment(link, "token");

        (await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, NewPassword)).Dispose();
        await api.WaitForEmailAsync(email, skip: 1);

        // The outbox and the audit table have no column that could hold either.
        await using OmniAiDbContext context = api.CreateDbContext();

        List<string> columns = await context.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                 WHERE table_name IN ('account_emails', 'security_events')
                """)
            .ToListAsync();

        // Exactly the approved columns (M2, M2a, M3): no address, token, link,
        // subject, body or password among them.
        Assert.Equal(
            [
                "attempts", "available_at", "claim_token", "claimed_at", "client_network_key", "failure_code", "id", "id",
                "invitation_id", "kind", "kind", "occurred_at", "requested_at", "sent_at", "settled_at", "status",
                "subject_key", "tenant_id", "tenant_id", "user_id", "user_id",
            ],
            columns.Order(StringComparer.Ordinal));

        // No log line, at any level, in any category, carries the token or the link.
        // Reported by category only, so a failure never prints the secret it found.
        Assert.Empty(CategoriesHolding(api.Logs.Lines, token));
        Assert.Empty(CategoriesHolding(api.Logs.Lines, link.Fragment));
        Assert.Empty(CategoriesHolding(api.Logs.Lines, NewPassword));
    }

    [Fact]
    public async Task The_reset_email_and_the_changed_email_follow_the_account_language()
    {
        string email = ApiAuthentication.UniqueEmail();

        using HttpClient owner = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(owner);
        using HttpResponseMessage created = await owner.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = ApiAuthentication.Password, language = "ar" });
        created.EnsureSuccessStatusCode();
        Guid userId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        using HttpClient visitor = api.CreateClient();
        (await AccountApiFixture.ForgotAsync(visitor, email)).Dispose();

        MimeMessage arabic = await api.WaitForEmailAsync(email);

        Assert.Equal("نبرة: تعيين كلمة مرور جديدة", arabic.Subject);
        Assert.StartsWith("نبرة\n", arabic.TextBody, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", arabic.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("30 دقيقة", arabic.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("omniai", arabic.TextBody + arabic.HtmlBody + arabic.Subject, StringComparison.OrdinalIgnoreCase);
        // One picture only: the mark, carried inside the message by Content-ID
        // (Phase 39.8a). Nothing remote, no inline SVG, no data: URI.
        string arabicHtml = arabic.HtmlBody ?? string.Empty;
        Assert.Single(arabicHtml.Split("<img", StringSplitOptions.None)[1..]);
        Assert.Contains("<img src=\"cid:nabra-mark@nabra\"", arabic.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(arabic.BodyParts, part => part.ContentId == "nabra-mark@nabra" && part.ContentType.MimeType == "image/png");
        Assert.DoesNotContain("data:", arabic.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", arabic.HtmlBody, StringComparison.OrdinalIgnoreCase);

        string token = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(arabic, "/reset-password"), "token");
        (await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, NewPassword)).Dispose();

        MimeMessage changed = await api.WaitForEmailAsync(email, skip: 1);
        Assert.Equal("نبرة: تم تغيير كلمة المرور", changed.Subject);
        Assert.Contains("dir=\"rtl\"", changed.HtmlBody, StringComparison.Ordinal);
    }

    private static List<string> CategoriesHolding(IEnumerable<string> lines, string secret) =>
        [.. lines
            .Where(line => line.Contains(secret, StringComparison.Ordinal))
            .Select(line => line.Split(' ', 2)[0])
            .Distinct(StringComparer.Ordinal)];

    private static string Shape(HttpResponseMessage response) =>
        string.Join(
            "|",
            response.Headers
                .Where(header => header.Key is not ("Date" or "Set-Cookie" or "traceparent"))
                .Select(header => header.Key)
                .Order(StringComparer.Ordinal)
                .Append(response.Content.Headers.ContentType?.ToString() ?? "-"));

    private static string WithoutTrace(string body)
    {
        using JsonDocument problem = JsonDocument.Parse(body);

        return JsonSerializer.Serialize(
            problem.RootElement.EnumerateObject()
                .Where(property => property.Name != "traceId")
                .ToDictionary(property => property.Name, property => property.Value.ToString()));
    }

    private static async Task<List<(Guid? User, string? Subject)>> RequestedEventsAsync(OmniAiDbContext context)
    {
        List<Requested> rows = await context.Database
            .SqlQuery<Requested>($"""
                SELECT user_id AS "UserId", subject_key AS "SubjectKey"
                  FROM security_events WHERE kind = 'auth.password_reset.requested'
                """)
            .ToListAsync();

        return [.. rows.Select(row => (row.UserId, row.SubjectKey))];
    }

    private static Task<List<string>> KindsForAsync(OmniAiDbContext context, Guid userId) =>
        context.Database
            .SqlQuery<string>($"SELECT kind AS \"Value\" FROM security_events WHERE user_id = {userId}")
            .ToListAsync();

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail("The condition did not become true.");
    }

    private sealed record Requested(Guid? UserId, string? SubjectKey);
}
