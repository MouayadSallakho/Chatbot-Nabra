using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OmniAI.Api.IntegrationTests.Realtime;
using OmniAI.Api.Realtime;
using OmniAI.Api.Security;
using OmniAI.Infrastructure.Identity;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// A password reset ends the account's realtime connections on every instance
/// (Phase 37.3), and a connection whose session is over can do nothing.
/// </summary>
public sealed class SessionRevocationApiTests(AccountApiFixture api) : IClassFixture<AccountApiFixture>
{
    [Fact]
    public async Task A_reset_on_one_instance_closes_the_account_s_hub_connections_on_both()
    {
        await using WebApplicationFactory<Program> second = api.CreateSecondInstance();

        (HttpClient owner, Guid tenantId, Guid userId) = await api.CreateOwnerAsync();
        using HttpClient ownerClient = owner;
        string email = await AiAgents.AiAgentApiFixture.CurrentEmailAsync(ownerClient);

        await using InboxHubClient onFirst = await InboxHubClient.SignInAndConnectAsync(api.Application, email);
        await using InboxHubClient onSecond = await InboxHubClient.SignInAndConnectAsync(second, email);

        // Both connections work before the reset.
        Assert.Null(await onFirst.InvokeAsync("Subscribe", tenantId));
        Assert.Null(await onSecond.InvokeAsync("Subscribe", tenantId));
        Assert.Equal(1, second.Services.GetRequiredService<SessionConnections>().CountFor(userId));

        using HttpClient visitor = api.CreateClient();
        (await AccountApiFixture.ForgotAsync(visitor, email)).Dispose();
        string token = AccountApiFixture.Fragment(
            AccountApiFixture.LinkTo(await api.WaitForEmailAsync(email), "/reset-password"),
            "token");

        using HttpResponseMessage reset = await AccountApiFixture.ResetAsync(visitor, userId.ToString("D"), token, "a-brand-new-passphrase-2026");
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // Closed here at once, and there through the shared Redis.
        Assert.True(await onFirst.WaitForCloseAsync(TimeSpan.FromSeconds(10)));
        Assert.True(await onSecond.WaitForCloseAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, second.Services.GetRequiredService<SessionConnections>().CountFor(userId));

        // And the HTTP session that owned the tenant is over too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await ownerClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_connection_whose_stamp_changed_is_refused_on_its_next_invocation_even_without_the_message()
    {
        (HttpClient owner, Guid tenantId, Guid userId) = await api.CreateOwnerAsync();
        using HttpClient ownerClient = owner;
        string email = await AiAgents.AiAgentApiFixture.CurrentEmailAsync(ownerClient);

        await using InboxHubClient hub = await InboxHubClient.SignInAndConnectAsync(api.Application, email);
        Assert.Null(await hub.InvokeAsync("Subscribe", tenantId));

        // The stamp moves without any revocation being published, as if the
        // message had been lost.
        await using (AsyncServiceScope scope = api.Services.CreateAsyncScope())
        {
            UserManager<OmniAiUser> users = scope.ServiceProvider.GetRequiredService<UserManager<OmniAiUser>>();
            OmniAiUser account = (await users.FindByIdAsync(userId.ToString("D")))!;

            await users.UpdateSecurityStampAsync(account);
            await scope.ServiceProvider.GetRequiredService<SessionStamps>().RecordAsync(userId, (await users.GetSecurityStampAsync(account))!);
        }

        Task<string?> refused = hub.InvokeAsync("Subscribe", tenantId);

        Assert.True(await hub.WaitForCloseAsync(TimeSpan.FromSeconds(10)));

        // The refusal may or may not reach the client before the transport goes.
        if (refused.IsCompletedSuccessfully)
        {
            Assert.Contains("session has ended", await refused ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
