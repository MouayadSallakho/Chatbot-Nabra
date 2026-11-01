using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>Deliberately tiny account limits, so each can be seen to fire.</summary>
public sealed class TightAccountLimitsFixture : AccountApiFixture
{
    protected override IReadOnlyDictionary<string, string> Limits => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AccountProtection:SignInFailuresPerAddressOnNetwork:Limit"] = "3",
        ["AccountProtection:SignInFailuresPerNetwork:Limit"] = "6",
        ["AccountProtection:SignInFailuresPerAddress:Limit"] = "8",
        ["AccountProtection:ResetRequestsPerAddress:Limit"] = "2",
        ["AccountProtection:ResetRequestsPerNetwork:Limit"] = "100",
        ["AccountProtection:ResetsPerNetwork:Limit"] = "3",
    };
}

/// <summary>
/// The account limits (Phase 37.3, rows 6 and 6a, as revised): sign-in counts
/// failures, per address on a network, per network and per address on every
/// network; reset requests per address and per network. Known and unknown
/// addresses alike, shared by every instance through Redis.
/// </summary>
/// <remarks>
/// Every test uses its own networks (the last octet), so the classes' shared
/// counters never mix.
/// </remarks>
public sealed class AccountProtectionApiTests(TightAccountLimitsFixture api) : IClassFixture<TightAccountLimitsFixture>
{
    [Fact]
    public async Task A_stranger_s_failures_on_one_network_do_not_stop_the_owner_on_another()
    {
        string email = await AccountAsync(Peer(10));

        // The attacker guesses from network A until it is throttled there.
        Assert.Equal(["401", "401", "401", "429"], await StatusesAsync(email, "a-wrong-guess-123", Peer(11), 4));

        // Even the right password is refused on A (the check comes first) ...
        using HttpClient onA = await api.CreateVisitorAsync(Peer(11));
        using HttpResponseMessage refused = await ApiAuthentication.SignInAsync(onA, email, ApiAuthentication.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        // ... while the owner, on network B, signs in.
        using HttpClient onB = await api.CreateVisitorAsync(Peer(12));
        using HttpResponseMessage owner = await ApiAuthentication.SignInAsync(onB, email, ApiAuthentication.Password);
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
    }

    [Fact]
    public async Task Sixty_successful_sign_ins_from_one_network_all_succeed()
    {
        // A support team behind one office address. The per-network failure limit
        // here is 6; successes count for nothing.
        List<string> team = [];

        for (int member = 0; member < 60; member++)
        {
            team.Add(await AccountAsync(Peer(20)));
        }

        foreach (string email in team)
        {
            using HttpClient client = await api.CreateVisitorAsync(Peer(21));
            using HttpResponseMessage signedIn = await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password);

            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        }
    }

    [Fact]
    public async Task Failures_reach_429_with_retry_after_identically_for_a_known_and_an_unknown_address()
    {
        string known = await AccountAsync(Peer(30));
        string unknown = ApiAuthentication.UniqueEmail();

        List<string> knownAnswers = await AnswersAsync(known, Peer(31), 4);
        List<string> unknownAnswers = await AnswersAsync(unknown, Peer(32), 4);

        Assert.Equal(["401:auth.credentials.invalid", "401:auth.credentials.invalid", "401:auth.credentials.invalid", "429:auth.throttled"], knownAnswers);
        Assert.Equal(knownAnswers, unknownAnswers);

        using HttpClient forKnown = await api.CreateVisitorAsync(Peer(31));
        using HttpClient forUnknown = await api.CreateVisitorAsync(Peer(32));
        using HttpResponseMessage knownRefusal = await ApiAuthentication.SignInAsync(forKnown, known, ApiAuthentication.Password);
        using HttpResponseMessage unknownRefusal = await ApiAuthentication.SignInAsync(forUnknown, unknown, ApiAuthentication.Password);

        Assert.Equal(WithoutTrace(await knownRefusal.Content.ReadAsStringAsync()), WithoutTrace(await unknownRefusal.Content.ReadAsStringAsync()));
        Assert.InRange(RetryAfter(knownRefusal), 1, 900);
        Assert.InRange(RetryAfter(unknownRefusal), 1, 900);

        await using OmniAiDbContext context = api.CreateDbContext();
        int throttled = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM security_events WHERE kind = 'auth.signin.throttled'")
            .SingleAsync();

        Assert.True(throttled >= 4);
    }

    [Fact]
    public async Task A_burst_of_parallel_guesses_cannot_overshoot_the_limit()
    {
        // The check reserves an in-flight slot atomically, so twenty guesses fired at
        // once cannot all pass it before the first failure is counted. The limit for
        // one address on one network here is 3.
        string email = await AccountAsync(Peer(85));

        HttpClient[] clients = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => api.CreateVisitorAsync(Peer(86))));

        HttpResponseMessage[] answers = await Task.WhenAll(clients.Select(client =>
            client.PostAsJsonAsync("/api/auth/login", new { email, password = "a-wrong-guess-123" })));

        int passedTheCheck = answers.Count(answer => answer.StatusCode == HttpStatusCode.Unauthorized);

        Assert.InRange(passedTheCheck, 1, 3);
        Assert.All(answers, answer => Assert.Contains(answer.StatusCode, (HttpStatusCode[])[HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests]));

        foreach (HttpResponseMessage answer in answers)
        {
            answer.Dispose();
        }

        foreach (HttpClient client in clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task The_per_address_ceiling_holds_across_networks()
    {
        string email = await AccountAsync(Peer(40));

        // Eight failures, two from each of four networks: no single network
        // reaches its limit of three, but the address reaches its ceiling of eight.
        foreach (int network in (int[])[41, 42, 43, 44])
        {
            Assert.Equal(["401", "401"], await StatusesAsync(email, "a-wrong-guess-123", Peer(network), 2));
        }

        // A fresh network, the right password: refused, because the ceiling is per
        // address.
        using HttpClient fresh = await api.CreateVisitorAsync(Peer(45));
        using HttpResponseMessage refused = await ApiAuthentication.SignInAsync(fresh, email, ApiAuthentication.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("auth.throttled", await AccountApiFixture.CodeOf(refused));
    }

    [Fact]
    public async Task Sign_in_failures_are_limited_per_network_and_another_network_is_unaffected()
    {
        List<string> answers = [];

        for (int attempt = 0; attempt < 7; attempt++)
        {
            using HttpClient client = await api.CreateVisitorAsync(Peer(50));
            using HttpResponseMessage response = await ApiAuthentication.SignInAsync(client, ApiAuthentication.UniqueEmail(), "whatever-password");
            answers.Add($"{(int)response.StatusCode}");
        }

        Assert.Equal(["401", "401", "401", "401", "401", "401", "429"], answers);

        using HttpClient elsewhere = await api.CreateVisitorAsync(Peer(51));
        using HttpResponseMessage fine = await ApiAuthentication.SignInAsync(elsewhere, ApiAuthentication.UniqueEmail(), "whatever-password");
        Assert.Equal(HttpStatusCode.Unauthorized, fine.StatusCode);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_its_address_s_failures_on_its_network()
    {
        string email = await AccountAsync(Peer(55));

        Assert.Equal(["401", "401"], await StatusesAsync(email, "a-wrong-guess-123", Peer(56), 2));

        using (HttpClient client = await api.CreateVisitorAsync(Peer(56)))
        using (HttpResponseMessage signedIn = await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password))
        {
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        }

        // The count on this network started again: three more failures before 429.
        Assert.Equal(["401", "401", "401", "429"], await StatusesAsync(email, "a-wrong-guess-123", Peer(56), 4));
    }

    [Fact]
    public async Task Two_instances_share_the_sign_in_failure_counters()
    {
        await using WebApplicationFactory<Program> second = api.CreateSecondInstance();

        string email = await AccountAsync(Peer(70));

        Assert.Equal(["401", "401"], await StatusesAsync(email, "a-wrong-guess-123", Peer(71), 2));

        using HttpClient onB = second.CreateClient();
        onB.DefaultRequestHeaders.Add(SimulatedPeerStartupFilter.HeaderName, Peer(71).ToString());
        using (HttpResponseMessage third = await ApiAuthentication.SignInAsync(onB, email, "a-wrong-guess-123"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, third.StatusCode);
        }

        // Back on the first instance, the right password: the three failures counted
        // on two instances throttle it.
        using HttpClient onA = await api.CreateVisitorAsync(Peer(71));
        using HttpResponseMessage refused = await ApiAuthentication.SignInAsync(onA, email, ApiAuthentication.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task Reset_requests_are_counted_once_across_two_instances()
    {
        await using WebApplicationFactory<Program> second = api.CreateSecondInstance();

        string known = ApiAuthentication.UniqueEmail();

        using (HttpClient owner = await api.CreateVisitorAsync(Peer(60)))
        {
            await ApiAuthentication.RegisterAsync(owner, known);
        }

        foreach (string address in (string[])[known, ApiAuthentication.UniqueEmail()])
        {
            using HttpClient onA = await api.CreateVisitorAsync(Peer(61));
            using HttpClient onB = second.CreateClient();
            onB.DefaultRequestHeaders.Add(SimulatedPeerStartupFilter.HeaderName, Peer(62).ToString());

            using HttpResponseMessage first = await AccountApiFixture.ForgotAsync(onA, address);
            using HttpResponseMessage secondRequest = await AccountApiFixture.ForgotAsync(onB, address);
            using HttpResponseMessage third = await AccountApiFixture.ForgotAsync(onA, address);

            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, secondRequest.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
            Assert.Equal("auth.throttled", await AccountApiFixture.CodeOf(third));
            Assert.True(third.Headers.Contains("Retry-After"));
        }
    }

    [Fact]
    public async Task Using_reset_links_is_limited_per_network()
    {
        List<HttpStatusCode> answers = [];

        for (int attempt = 0; attempt < 4; attempt++)
        {
            using HttpClient client = await api.CreateVisitorAsync(Peer(80));
            using HttpResponseMessage response = await AccountApiFixture.ResetAsync(client, Guid.NewGuid().ToString("D"), "not-a-token", "a-long-enough-passphrase");
            answers.Add(response.StatusCode);
        }

        Assert.Equal(
            [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests],
            answers);
    }

    /// <summary>A new account, registered from a network; returns its address.</summary>
    private async Task<string> AccountAsync(IPAddress network)
    {
        string email = ApiAuthentication.UniqueEmail();

        using HttpClient owner = await api.CreateVisitorAsync(network);
        await ApiAuthentication.RegisterAsync(owner, email);

        return email;
    }

    private async Task<List<string>> StatusesAsync(string email, string password, IPAddress network, int attempts)
    {
        List<string> answers = [];

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            using HttpClient client = await api.CreateVisitorAsync(network);
            using HttpResponseMessage response = await ApiAuthentication.SignInAsync(client, email, password);
            answers.Add($"{(int)response.StatusCode}");
        }

        return answers;
    }

    private async Task<List<string>> AnswersAsync(string email, IPAddress network, int attempts)
    {
        List<string> answers = [];

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            using HttpClient client = await api.CreateVisitorAsync(network);
            using HttpResponseMessage response = await ApiAuthentication.SignInAsync(client, email, "a-wrong-guess-123");

            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            answers.Add($"{(int)response.StatusCode}:{problem.RootElement.GetProperty("code").GetString()}");
        }

        return answers;
    }

    private static int RetryAfter(HttpResponseMessage response) =>
        int.Parse(response.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);

    private static string WithoutTrace(string body)
    {
        using JsonDocument problem = JsonDocument.Parse(body);

        return JsonSerializer.Serialize(
            problem.RootElement.EnumerateObject()
                .Where(property => property.Name != "traceId")
                .ToDictionary(property => property.Name, property => property.Value.ToString()));
    }

    private static IPAddress Peer(int last) => IPAddress.Parse($"198.51.100.{last}");
}

/// <summary>A host whose protection Redis cannot be reached.</summary>
public sealed class UnreachableAccountProtectionFixture : AccountApiFixture
{
    protected override IReadOnlyDictionary<string, string> Limits => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // A closed port on this machine: every limiter call fails.
        ["ConnectionStrings:OmniAiProtectionRedis"] = "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=500,asyncTimeout=500",
    };
}

/// <summary>Fail closed: no limiter, no sign-in and no reset (Phase 37.3).</summary>
public sealed class UnreachableAccountProtectionApiTests(UnreachableAccountProtectionFixture api) : IClassFixture<UnreachableAccountProtectionFixture>
{
    [Fact]
    public async Task Sign_in_and_reset_are_refused_with_503_when_the_limiter_cannot_decide()
    {
        using HttpClient client = await api.CreateVisitorAsync();

        using HttpResponseMessage signIn = await ApiAuthentication.SignInAsync(client, ApiAuthentication.UniqueEmail(), "whatever-password");
        using HttpResponseMessage forgot = await AccountApiFixture.ForgotAsync(client, ApiAuthentication.UniqueEmail());
        using HttpResponseMessage reset = await AccountApiFixture.ResetAsync(client, Guid.NewGuid().ToString("D"), "x", "a-long-enough-passphrase");

        foreach (HttpResponseMessage refusal in (HttpResponseMessage[])[signIn, forgot, reset])
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refusal.StatusCode);
            Assert.Equal("auth.throttle.unavailable", await AccountApiFixture.CodeOf(refusal));
            Assert.DoesNotContain("redis", await refusal.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>
/// No hard lockout (Phase 37.3 Revision 1, D-2): with the limits out of the way,
/// any number of failures leaves the account usable and unlocked.
/// </summary>
public sealed class NoLockoutApiTests(AccountApiFixture api) : IClassFixture<AccountApiFixture>
{
    [Fact]
    public async Task No_account_is_locked_after_many_failures()
    {
        string email = ApiAuthentication.UniqueEmail();

        using (HttpClient owner = await api.CreateVisitorAsync())
        {
            await ApiAuthentication.RegisterAsync(owner, email);
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            using HttpClient guessing = await api.CreateVisitorAsync();
            using HttpResponseMessage rejected = await ApiAuthentication.SignInAsync(guessing, email, $"a-wrong-guess-{attempt}");

            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }

        using HttpClient client = await api.CreateVisitorAsync();
        using HttpResponseMessage signedIn = await ApiAuthentication.SignInAsync(client, email, ApiAuthentication.Password);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);

        await using OmniAiDbContext context = api.CreateDbContext();
        int locked = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM users WHERE email = {email} AND (lockout_end IS NOT NULL OR access_failed_count > 0)")
            .SingleAsync();

        Assert.Equal(0, locked);
    }
}
