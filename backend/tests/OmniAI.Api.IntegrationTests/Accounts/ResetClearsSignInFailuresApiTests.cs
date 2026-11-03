using System.Net;
using MimeKit;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// Phase 37.5b, the owner's decision after 37.3: a completed password reset clears
/// that address's sign-in failures (its ceiling across every network, and its
/// count on the network the reset came from), so the person can sign in at once;
/// and never another address's counters, nor the network's.
/// </summary>
/// <remarks>
/// On the tight limits: 3 failures per address on a network, 6 per network, 8 per
/// address. Every test uses its own networks (the last octet).
/// </remarks>
public sealed class ResetClearsSignInFailuresApiTests(TightAccountLimitsFixture api)
    : IClassFixture<TightAccountLimitsFixture>
{
    private const string Wrong = "a-wrong-guess-123";
    private const string NewPassword = "a-brand-new-passphrase-2026";

    [Fact]
    public async Task After_a_reset_the_address_signs_in_at_once_on_the_network_it_was_throttled_on()
    {
        (string email, Guid userId) = await AccountAsync(Peer(110));

        Assert.Equal(["401", "401", "401", "429"], await StatusesAsync(email, Wrong, Peer(111), 4));

        await ResetAsync(email, userId, Peer(111));

        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(email, NewPassword, Peer(111)));
    }

    [Fact]
    public async Task After_a_reset_the_address_ceiling_across_networks_is_lifted()
    {
        (string email, Guid userId) = await AccountAsync(Peer(140));

        foreach (int network in (int[])[141, 142, 143, 144])
        {
            Assert.Equal(["401", "401"], await StatusesAsync(email, Wrong, Peer(network), 2));
        }

        // Eight failures: the ceiling holds on a fresh network.
        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInStatusAsync(email, ApiAuthentication.Password, Peer(145)));

        await ResetAsync(email, userId, Peer(145));

        Assert.Equal(HttpStatusCode.OK, await SignInStatusAsync(email, NewPassword, Peer(146)));
    }

    [Fact]
    public async Task A_reset_never_clears_another_address_s_counters()
    {
        (string resetting, Guid resettingId) = await AccountAsync(Peer(150));
        (string other, _) = await AccountAsync(Peer(150));

        // The other address: full on network 151, and at its ceiling overall.
        Assert.Equal(["401", "401", "401"], await StatusesAsync(other, Wrong, Peer(151), 3));
        Assert.Equal(["401", "401"], await StatusesAsync(other, Wrong, Peer(152), 2));
        Assert.Equal(["401", "401"], await StatusesAsync(other, Wrong, Peer(153), 2));
        Assert.Equal(["401"], await StatusesAsync(other, Wrong, Peer(154), 1));

        // Somebody else resets their password from that same network.
        await ResetAsync(resetting, resettingId, Peer(151));

        // The other address is still refused, on that network and on a fresh one.
        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInStatusAsync(other, ApiAuthentication.Password, Peer(151)));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInStatusAsync(other, ApiAuthentication.Password, Peer(155)));
    }

    [Fact]
    public async Task A_reset_leaves_the_network_s_own_counter_alone()
    {
        (string email, Guid userId) = await AccountAsync(Peer(160));

        // Five failures for unknown addresses on network 161: one short of its six.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(ApiAuthentication.UniqueEmail(), Wrong, Peer(161)));
        }

        await ResetAsync(email, userId, Peer(161));

        // Had the reset cleared the network, both of these would be 401.
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInStatusAsync(ApiAuthentication.UniqueEmail(), Wrong, Peer(161)));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInStatusAsync(ApiAuthentication.UniqueEmail(), Wrong, Peer(161)));
    }

    [Fact]
    public async Task A_failed_reset_clears_nothing()
    {
        (string email, Guid userId) = await AccountAsync(Peer(170));

        Assert.Equal(["401", "401", "401", "429"], await StatusesAsync(email, Wrong, Peer(171), 4));

        using HttpClient visitor = await api.CreateVisitorAsync(Peer(171));
        using HttpResponseMessage rejected = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), "not-a-token", NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInStatusAsync(email, ApiAuthentication.Password, Peer(171)));
    }

    // ---- arranging ---------------------------------------------------------

    private async Task<(string Email, Guid UserId)> AccountAsync(IPAddress network)
    {
        string email = ApiAuthentication.UniqueEmail();

        using HttpClient owner = await api.CreateVisitorAsync(network);
        Guid userId = await ApiAuthentication.RegisterAsync(owner, email);

        return (email, userId);
    }

    /// <summary>Asks for a reset link and uses it, both from one network.</summary>
    private async Task ResetAsync(string email, Guid userId, IPAddress network)
    {
        using HttpClient visitor = await api.CreateVisitorAsync(network);

        (await AccountApiFixture.ForgotAsync(visitor, email)).Dispose();

        MimeMessage message = await api.WaitForEmailAsync(email);
        string token = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(message, "/reset-password"), "token");

        using HttpResponseMessage reset = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, NewPassword);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    private async Task<HttpStatusCode> SignInStatusAsync(string email, string password, IPAddress network)
    {
        using HttpClient client = await api.CreateVisitorAsync(network);
        using HttpResponseMessage response = await ApiAuthentication.SignInAsync(client, email, password);

        return response.StatusCode;
    }

    private async Task<List<string>> StatusesAsync(string email, string password, IPAddress network, int attempts)
    {
        List<string> answers = [];

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            answers.Add($"{(int)await SignInStatusAsync(email, password, network)}");
        }

        return answers;
    }

    private static IPAddress Peer(int last) => IPAddress.Parse($"198.51.100.{last}");
}
