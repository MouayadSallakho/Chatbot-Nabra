using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OmniAI.Api.IntegrationTests.Tenants;

namespace OmniAI.Api.IntegrationTests.Auth;

/// <summary>
/// Registration, sign-in, sign-out and the current account, over real HTTP
/// against real PostgreSQL.
/// </summary>
/// <remarks>
/// Nothing here is substituted. Passwords are hashed by the real Identity
/// hasher, stored in the real <c>users</c> table, and verified by the real
/// <c>SignInManager</c>; the cookie is the one the framework issued. A suite that
/// faked any of that would be proving a test double works.
/// </remarks>
public sealed class AuthEndpointsTests(TenantApiFixture api) : IClassFixture<TenantApiFixture>
{
    [Fact]
    public async Task An_account_can_be_registered_and_is_signed_in_immediately()
    {
        // Signing the new account in is a deliberate decision: somebody who just
        // chose the password has told us everything a sign-in would. It is only
        // defensible while email confirmation is not required.
        using HttpClient client = api.CreateClient();

        Guid userId = await ApiAuthentication.RegisterAsync(client);

        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(userId, await IdOfAsync(me));
    }

    [Fact]
    public async Task The_stored_password_is_not_the_password()
    {
        // The single most important assertion in this file. It reads the column
        // directly rather than trusting that Identity did the right thing.
        using HttpClient client = api.CreateClient();

        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(client, email);

        object? hash = await api.Database.ExecuteScalarAsync(
            $"SELECT password_hash FROM users WHERE email = '{email}'");

        string stored = Assert.IsType<string>(hash);

        Assert.NotEqual(ApiAuthentication.Password, stored);
        Assert.DoesNotContain(ApiAuthentication.Password, stored, StringComparison.Ordinal);

        // Not reversible and not a bare digest either: a modern Identity hash is a
        // base64 blob carrying its format marker, salt and iteration count, so it
        // is comfortably longer than any plain digest would be. The exact contents
        // are deliberately not asserted -- that would pin the test to a hashing
        // format this code does not own and should be free to upgrade.
        Assert.True(stored.Length > 40, "The stored value is too short to be a salted hash.");
    }

    [Fact]
    public async Task A_correct_password_signs_in()
    {
        using HttpClient registering = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(registering, email);

        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_grants_no_session()
    {
        using HttpClient registering = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(registering, email);

        using HttpClient client = api.CreateClient();

        using HttpResponseMessage response =
            await ApiAuthentication.SignInAsync(client, email, "not-the-right-password");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task An_unknown_address_and_a_wrong_password_are_indistinguishable()
    {
        // The login form must not become an account-existence oracle. Same status,
        // same title, same detail -- if these ever diverge, somebody can learn
        // which of a list of addresses are registered here just by trying them.
        using HttpClient registering = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(registering, email);

        using HttpClient first = api.CreateClient();
        using HttpClient second = api.CreateClient();

        using HttpResponseMessage wrongPassword =
            await ApiAuthentication.SignInAsync(first, email, "not-the-right-password");

        using HttpResponseMessage unknownAccount = await ApiAuthentication.SignInAsync(
            second,
            ApiAuthentication.UniqueEmail(),
            ApiAuthentication.Password);

        Assert.Equal(unknownAccount.StatusCode, wrongPassword.StatusCode);

        (string wrongTitle, string wrongDetail) = await ProblemAsync(wrongPassword);
        (string unknownTitle, string unknownDetail) = await ProblemAsync(unknownAccount);

        Assert.Equal(unknownTitle, wrongTitle);
        Assert.Equal(unknownDetail, wrongDetail);

        // And the shared message names neither possibility.
        Assert.DoesNotContain("exist", wrongDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("found", wrongDetail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked", wrongDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repeated_failures_are_throttled_on_their_network_and_never_lock_the_account()
    {
        // Phase 37.3 Revision 1 (D-2, no hard lockout). Five failures from this
        // network, then the sixth attempt uses the CORRECT password: it is refused
        // by the failure limiter (429, before the password is checked), not by an
        // account lockout, and the account itself is not locked -- from any other
        // network its owner signs in (AccountProtectionApiTests).
        using HttpClient registering = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(registering, email);

        for (int attempt = 0; attempt < 5; attempt++)
        {
            using HttpClient failing = api.CreateClient();
            using HttpResponseMessage rejected =
                await ApiAuthentication.SignInAsync(failing, email, $"wrong-password-{attempt}");

            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        using HttpClient client = api.CreateClient();
        using HttpResponseMessage throttled =
            await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        Assert.True(throttled.Headers.Contains("Retry-After"));

        (_, string detail) = await ProblemAsync(throttled);

        Assert.DoesNotContain("lock", detail, StringComparison.OrdinalIgnoreCase);

        // Identity's lockout was never applied: no lockout end, no failure count.
        object? locked = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM users WHERE email = '{email}' AND (lockout_end IS NOT NULL OR access_failed_count > 0)");

        Assert.Equal(0L, locked as long?);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("elevenchars")]
    public async Task A_password_below_the_minimum_length_is_rejected(string password)
    {
        // Twelve characters, and no composition rules. The policy is asserted at
        // its boundary so that a change to it is a deliberate edit here rather
        // than something nobody notices.
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = ApiAuthentication.UniqueEmail(), password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_twelve_character_password_with_no_symbols_or_digits_is_accepted()
    {
        // The other half of the policy: length is the requirement, and arbitrary
        // composition rules are deliberately absent. A test that only checked the
        // rejection would pass just as well against a policy demanding punctuation.
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            // Twelve lowercase letters: no digit, no capital, no punctuation. If any
            // composition rule had crept into the policy, this would be rejected.
            new { email = ApiAuthentication.UniqueEmail(), password = "abcdefghijkl" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task An_address_that_is_already_registered_is_refused()
    {
        using HttpClient first = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(first, email);

        using HttpClient second = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(second);

        using HttpResponseMessage response = await second.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = ApiAuthentication.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        object? accounts = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM users WHERE email = '{email}'");

        Assert.Equal(1L, accounts as long?);
    }

    [Fact]
    public async Task An_address_differing_only_in_case_is_the_same_address()
    {
        // Email is the login identifier, and people do not think of it as
        // case-sensitive. Identity normalises it and the unique index is on the
        // normalised column, so this is refused rather than creating a second
        // account somebody could never reliably sign in to.
        using HttpClient first = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(first, email);

        using HttpClient second = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(second);

        using HttpResponseMessage response = await second.PostAsJsonAsync(
            "/api/auth/register",
            new { email = email.ToUpperInvariant(), password = ApiAuthentication.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_current_account_response_carries_four_fields_and_no_credential()
    {
        // Written out by hand rather than serialised from the Identity entity, so
        // that adding a column there cannot quietly add a field here. The password
        // hash, the security stamp and the lockout state must never appear. Phase
        // 37.3 added the third field on purpose: the stored language; Phase 39.1
        // the fourth: the display name, null until the person gives one.
        using HttpClient client = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(client, email);

        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        string body = await me.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Equal(
            ["id", "email", "language", "displayName"],
            document.RootElement.EnumerateObject().Select(field => field.Name));

        foreach (string forbidden in
            (string[])["passwordHash", "securityStamp", "concurrencyStamp", "lockout",
                       "accessFailed", "twoFactor", "normalized"])
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(ApiAuthentication.Password, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_anonymous_caller_is_not_told_who_they_are()
    {
        using HttpClient client = api.CreateClient();

        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.RegisterAsync(client);

        using HttpResponseMessage signedOut = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);

        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Signing_out_requires_a_session_of_its_own()
    {
        // An endpoint any site could trigger would let a third party sign people
        // out of this application at will.
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpResponseMessage response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_same_account_can_sign_in_again_after_signing_out()
    {
        // Sign-out ends a session; it does not disable the account.
        using HttpClient client = api.CreateClient();
        string email = ApiAuthentication.UniqueEmail();
        await ApiAuthentication.RegisterAsync(client, email);

        using (HttpResponseMessage signedOut = await client.PostAsync("/api/auth/logout", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
        }

        using HttpClient again = api.CreateClient();
        using HttpResponseMessage back =
            await ApiAuthentication.SignInAsync(again, email, ApiAuthentication.Password);

        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }

    [Fact]
    public async Task Registration_creates_an_account_and_nothing_else()
    {
        // No tenant is invented from the address, no membership is granted, and no
        // role is assigned -- there are no roles. A brand new account owns nothing.
        using HttpClient client = api.CreateClient();

        Guid userId = await ApiAuthentication.RegisterAsync(client);

        object? memberships = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenant_memberships WHERE user_id = '{userId}'");

        Assert.Equal(0L, memberships as long?);

        using HttpResponseMessage workspaces = await client.GetAsync("/api/me/tenants");
        using JsonDocument body =
            JsonDocument.Parse(await workspaces.Content.ReadAsStringAsync());

        Assert.Empty(body.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Registration_cannot_be_used_to_ask_for_a_tenant_or_a_role()
    {
        // The request type has two fields, so extra ones are simply not bound.
        // Asserted anyway: a future request record gaining a field would be a
        // privilege-escalation endpoint with extra steps.
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        Guid invented = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = ApiAuthentication.UniqueEmail(),
                password = ApiAuthentication.Password,
                tenantId = invented,
                role = "administrator",
                isAdmin = true,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        Guid userId = await IdOfAsync(response);

        object? memberships = await api.Database.ExecuteScalarAsync(
            $"SELECT count(*) FROM tenant_memberships WHERE user_id = '{userId}'");

        Assert.Equal(0L, memberships as long?);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Registration_without_an_address_is_rejected(string? email)
    {
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = ApiAuthentication.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>The account id in an auth response body.</summary>
    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>The title and detail of a Problem Details body.</summary>
    private static async Task<(string Title, string Detail)> ProblemAsync(
        HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (
            body.RootElement.GetProperty("title").GetString() ?? string.Empty,
            body.RootElement.GetProperty("detail").GetString() ?? string.Empty);
    }
}
