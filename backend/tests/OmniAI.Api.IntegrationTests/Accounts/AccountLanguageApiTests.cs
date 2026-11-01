using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// Each person's stored language (Phase 37.3, row 7), and the codes on sign-in and
/// registration refusals (row 6b).
/// </summary>
public sealed class AccountLanguageApiTests(AccountApiFixture api) : IClassFixture<AccountApiFixture>
{
    [Theory]
    [InlineData("ar", null, "ar")]
    [InlineData("en", "ar-SA,ar;q=0.9", "en")]
    [InlineData(null, "ar-SA;q=0.9, en;q=0.8", "ar")]
    [InlineData(null, "fr-FR, en;q=0.5", "en")]
    [InlineData(null, "fr-FR", null)]
    [InlineData(null, null, null)]
    public async Task Sign_up_stores_the_field_else_the_header_else_nothing(string? field, string? header, string? stored)
    {
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = ApiAuthentication.UniqueEmail(), password = ApiAuthentication.Password, language = field }),
        };

        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", header);
        }

        using HttpResponseMessage created = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Assert.Equal(stored, await LanguageAsync(client));
    }

    [Fact]
    public async Task Sign_in_fills_an_empty_language_and_never_replaces_a_chosen_one()
    {
        string email = ApiAuthentication.UniqueEmail();

        using (HttpClient first = api.CreateClient())
        {
            await ApiAuthentication.RegisterAsync(first, email);
            Assert.Null(await LanguageAsync(first));
        }

        using HttpClient arabic = await SignInWithHeaderAsync(email, "ar");
        Assert.Equal("ar", await LanguageAsync(arabic));

        // A later sign-in from an English browser leaves the choice alone.
        using HttpClient english = await SignInWithHeaderAsync(email, "en-GB");
        Assert.Equal("ar", await LanguageAsync(english));
    }

    [Fact]
    public async Task The_person_changes_it_with_a_put_and_only_en_or_ar_is_accepted()
    {
        using HttpClient client = await api.CreateSignedInClientAsync();

        using HttpResponseMessage toArabic = await client.PutAsJsonAsync("/api/me/language", new { language = "ar" });
        Assert.Equal(HttpStatusCode.NoContent, toArabic.StatusCode);
        Assert.Equal("ar", await LanguageAsync(client));

        using HttpResponseMessage toEnglish = await client.PutAsJsonAsync("/api/me/language", new { language = "en" });
        Assert.Equal(HttpStatusCode.NoContent, toEnglish.StatusCode);
        Assert.Equal("en", await LanguageAsync(client));

        foreach (object body in (object[])[new { language = "fr" }, new { language = "AR" }, new { language = "ar-SA" }, new { language = (string?)null }])
        {
            using HttpResponseMessage refused = await client.PutAsJsonAsync("/api/me/language", body);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("language.unsupported", await AccountApiFixture.CodeOf(refused));
        }

        Assert.Equal("en", await LanguageAsync(client));
    }

    [Fact]
    public async Task The_put_needs_a_session_and_an_antiforgery_token()
    {
        using HttpClient anonymous = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(anonymous);

        using HttpResponseMessage noSession = await anonymous.PutAsJsonAsync("/api/me/language", new { language = "ar" });
        Assert.Equal(HttpStatusCode.Unauthorized, noSession.StatusCode);

        using HttpClient signedIn = await api.CreateSignedInClientAsync();
        signedIn.DefaultRequestHeaders.Remove(ApiAuthentication.AntiforgeryHeader);

        using HttpResponseMessage noToken = await signedIn.PutAsJsonAsync("/api/me/language", new { language = "ar" });
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal("antiforgery.invalid", await AccountApiFixture.CodeOf(noToken));
    }

    [Fact]
    public async Task An_unsupported_language_on_sign_up_or_sign_in_is_refused_with_its_code()
    {
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpResponseMessage register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = ApiAuthentication.UniqueEmail(), password = ApiAuthentication.Password, language = "fr" });
        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);
        Assert.Equal("language.unsupported", await AccountApiFixture.CodeOf(register));

        using HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = ApiAuthentication.UniqueEmail(), password = "x", language = "de" });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal("language.unsupported", await AccountApiFixture.CodeOf(login));
    }

    [Fact]
    public async Task An_unknown_address_and_a_wrong_password_are_the_same_refusal_byte_for_byte()
    {
        string email = ApiAuthentication.UniqueEmail();

        using (HttpClient owner = api.CreateClient())
        {
            await ApiAuthentication.RegisterAsync(owner, email);
        }

        using HttpClient client = api.CreateClient();
        using HttpResponseMessage wrong = await ApiAuthentication.SignInAsync(client, email, "not-the-password-at-all");
        using HttpResponseMessage unknown = await ApiAuthentication.SignInAsync(client, ApiAuthentication.UniqueEmail(), "not-the-password-at-all");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(WithoutTrace(await wrong.Content.ReadAsStringAsync()), WithoutTrace(await unknown.Content.ReadAsStringAsync()));
        Assert.Equal("auth.credentials.invalid", await AccountApiFixture.CodeOf(wrong));
    }

    [Theory]
    [InlineData("", "correct-horse-battery-staple", "auth.registration.incomplete")]
    [InlineData("valid-{0}@example.test", "short", "auth.password.too_short")]
    [InlineData("not-an-address-{0}", "correct-horse-battery-staple", "auth.email.invalid")]
    public async Task Every_registration_refusal_carries_a_code(string emailFormat, string password, string code)
    {
        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        string email = string.Format(System.Globalization.CultureInfo.InvariantCulture, emailFormat, Guid.NewGuid().ToString("N"));

        using HttpResponseMessage refused = await client.PostAsJsonAsync("/api/auth/register", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(code, await AccountApiFixture.CodeOf(refused));
    }

    [Fact]
    public async Task A_taken_address_is_refused_with_its_code_and_the_old_detail()
    {
        string email = ApiAuthentication.UniqueEmail();

        using (HttpClient owner = api.CreateClient())
        {
            await ApiAuthentication.RegisterAsync(owner, email);
        }

        using HttpClient client = api.CreateClient();
        await ApiAuthentication.BootstrapAntiforgeryAsync(client);

        using HttpResponseMessage refused = await client.PostAsJsonAsync("/api/auth/register", new { email, password = ApiAuthentication.Password });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("auth.email.taken", await AccountApiFixture.CodeOf(refused));

        using JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("Registration rejected", problem.RootElement.GetProperty("title").GetString());
    }

    private async Task<HttpClient> SignInWithHeaderAsync(string email, string acceptLanguage)
    {
        HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", acceptLanguage);

        using HttpResponseMessage signedIn = await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password);
        signedIn.EnsureSuccessStatusCode();

        return client;
    }

    private static async Task<string?> LanguageAsync(HttpClient client)
    {
        using HttpResponseMessage me = await client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await me.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("language").GetString();
    }

    private static string WithoutTrace(string body)
    {
        using JsonDocument problem = JsonDocument.Parse(body);

        return JsonSerializer.Serialize(
            problem.RootElement.EnumerateObject()
                .Where(property => property.Name != "traceId")
                .ToDictionary(property => property.Name, property => property.Value.ToString()));
    }
}
