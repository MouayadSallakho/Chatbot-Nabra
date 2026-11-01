using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using OmniAI.Infrastructure.Persistence;

namespace OmniAI.Api.IntegrationTests.Accounts;

/// <summary>
/// Invitations are emailed, in the invitation's language (Phase 37.3, D-1 option A,
/// row 8a, migration M2a).
/// </summary>
public sealed class InvitationEmailApiTests(AccountApiFixture api) : IClassFixture<AccountApiFixture>
{
    [Fact]
    public async Task One_email_per_invitation_in_the_language_asked_for_and_its_link_accepts()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync("Northwind <Trading>");
        using HttpClient ownerClient = owner;
        string invitee = Unique();

        using HttpResponseMessage created = await InviteAsync(ownerClient, tenantId, invitee, "ar");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        MimeMessage email = await api.WaitForEmailAsync(invitee);

        Assert.Equal("نبرة: تلقّيت دعوة إلى مساحة عمل", email.Subject);
        Assert.Contains("<html lang=\"ar\" dir=\"rtl\">", email.HtmlBody, StringComparison.Ordinal);

        // The workspace's name is encoded and isolated, never markup.
        Assert.Contains("<bdi>Northwind &lt;Trading&gt;</bdi>", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("\u2068Northwind <Trading>\u2069", email.TextBody, StringComparison.Ordinal);

        string token = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(email, "/accept-invitation"), "token");

        // The emailed token is not the copied one, and only its hash is stored.
        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.NotEqual(body.RootElement.GetProperty("token").GetString(), token);

        await Task.Delay(1000);
        Assert.Equal(1, api.EmailsTo(invitee));

        (HttpClient account, _) = await api.CreateAccountAsync(invitee);
        using HttpClient accountClient = account;

        using HttpResponseMessage accepted = await accountClient.PostAsJsonAsync("/api/invitations/accept", new { token, language = "ar" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // Acceptance filled the new account's empty language.
        using JsonDocument me = JsonDocument.Parse(await accountClient.GetStringAsync("/api/auth/me"));
        Assert.Equal("ar", me.RootElement.GetProperty("language").GetString());

        await using OmniAiDbContext context = api.CreateDbContext();
        int events = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM security_events WHERE kind = 'auth.invitation.email_sent' AND tenant_id = {tenantId}")
            .SingleAsync();
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task Without_a_language_the_invitation_takes_the_inviter_s_and_the_copied_link_still_accepts()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        using HttpClient ownerClient = owner;

        using (HttpResponseMessage chosen = await ownerClient.PutAsJsonAsync("/api/me/language", new { language = "ar" }))
        {
            Assert.Equal(HttpStatusCode.NoContent, chosen.StatusCode);
        }

        string invitee = Unique();
        using HttpResponseMessage created = await InviteAsync(ownerClient, tenantId, invitee, null);
        created.EnsureSuccessStatusCode();

        MimeMessage email = await api.WaitForEmailAsync(invitee);
        Assert.StartsWith("نبرة: ", email.Subject, StringComparison.Ordinal);

        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        string copied = body.RootElement.GetProperty("token").GetString()!;

        (HttpClient account, _) = await api.CreateAccountAsync(invitee);
        using HttpClient accountClient = account;

        using HttpResponseMessage accepted = await accountClient.PostAsJsonAsync("/api/invitations/accept", new { token = copied });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // Once accepted, the emailed token is spent with it.
        string emailed = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(email, "/accept-invitation"), "token");
        using HttpResponseMessage again = await accountClient.PostAsJsonAsync("/api/invitations/accept", new { token = emailed });
        Assert.Equal(HttpStatusCode.Gone, again.StatusCode);
    }

    [Fact]
    public async Task A_revoked_invitation_refuses_both_tokens_and_a_wrong_one_is_absence()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        using HttpClient ownerClient = owner;
        string invitee = Unique();

        using HttpResponseMessage created = await InviteAsync(ownerClient, tenantId, invitee, "en");
        created.EnsureSuccessStatusCode();
        using JsonDocument body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        string copied = body.RootElement.GetProperty("token").GetString()!;
        Guid invitationId = body.RootElement.GetProperty("invitation").GetProperty("id").GetGuid();

        MimeMessage email = await api.WaitForEmailAsync(invitee);
        Assert.Equal("Nabra: You have been invited to a workspace", email.Subject);
        string emailed = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(email, "/accept-invitation"), "token");

        using (HttpResponseMessage revoked = await ownerClient.PostAsync($"/api/tenants/{tenantId}/invitations/{invitationId}/revoke", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }

        (HttpClient account, _) = await api.CreateAccountAsync(invitee);
        using HttpClient accountClient = account;

        foreach (string token in (string[])[copied, emailed])
        {
            using HttpResponseMessage refused = await accountClient.PostAsJsonAsync("/api/invitations/accept", new { token });
            Assert.Equal(HttpStatusCode.Gone, refused.StatusCode);
        }

        using HttpResponseMessage wrong = await accountClient.PostAsJsonAsync("/api/invitations/accept", new { token = new string('A', 43) });
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
    }

    [Fact]
    public async Task An_unsupported_language_is_refused_and_nothing_is_queued()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        using HttpClient ownerClient = owner;
        string invitee = Unique();

        using HttpResponseMessage refused = await InviteAsync(ownerClient, tenantId, invitee, "fr");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("language.unsupported", await AccountApiFixture.CodeOf(refused));

        await using OmniAiDbContext context = api.CreateDbContext();
        int invitations = await context.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM tenant_invitations WHERE tenant_id = {tenantId}")
            .SingleAsync();
        Assert.Equal(0, invitations);
    }

    [Fact]
    public async Task Another_workspace_never_sees_the_invitation_and_no_token_reaches_a_log()
    {
        (HttpClient owner, Guid tenantId, _) = await api.CreateOwnerAsync();
        using HttpClient ownerClient = owner;
        (HttpClient rival, _, _) = await api.CreateOwnerAsync("Rival");
        using HttpClient rivalClient = rival;

        string invitee = Unique();
        using HttpResponseMessage created = await InviteAsync(ownerClient, tenantId, invitee, "en");
        created.EnsureSuccessStatusCode();

        // Another workspace's owner cannot invite into this one, or read it.
        using HttpResponseMessage foreign = await InviteAsync(rivalClient, tenantId, Unique(), "en");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        MimeMessage email = await api.WaitForEmailAsync(invitee);
        string emailed = AccountApiFixture.Fragment(AccountApiFixture.LinkTo(email, "/accept-invitation"), "token");

        Assert.DoesNotContain(api.Logs.Lines, line => line.Contains(emailed, StringComparison.Ordinal));
    }

    private static string Unique() => $"invitee-{Guid.NewGuid():N}@example.test";

    private static Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid tenantId, string email, string? language) =>
        client.PostAsJsonAsync(
            $"/api/tenants/{tenantId}/invitations",
            new { email, roleIds = Array.Empty<Guid>(), language });
}
