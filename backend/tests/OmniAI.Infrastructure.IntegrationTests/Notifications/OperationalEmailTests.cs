using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.DependencyInjection;
using OmniAI.Infrastructure.IntegrationTests.Reports;
using OmniAI.Infrastructure.Notifications;
using OmniAI.Infrastructure.Notifications.Email;
using OmniAI.Infrastructure.Persistence;
using OmniAI.Testing;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// Operational email: the outbox, the send-time re-checks, the SMTP adapter over
/// a real loopback server, and the Development live-email guard.
/// </summary>
/// <remarks>
/// No test here reaches a real mail server. The SMTP sink listens on 127.0.0.1;
/// the non-loopback hosts are reserved <c>.invalid</c> names that the guard must
/// stop before any connection is attempted.
/// </remarks>
public sealed class OperationalEmailTests(PostgresTestDatabase database)
    : IClassFixture<PostgresTestDatabase>
{
    // --------------------------------------------------------------- outbox

    [Fact]
    public async Task A_queued_email_is_sent_once_and_recorded_as_sent()
    {
        (ReportWorld world, NotificationHarness harness) = await QueuedAsync(PlatformPermissions.AnalyticsView);

        IReadOnlyList<NotificationEmailStatus?> statuses = await harness.SendAllEmailsAsync();

        Assert.Equal([NotificationEmailStatus.Sent], statuses);

        OperationalEmailMessage message = Assert.Single(harness.Email.Sent);
        Assert.Equal("OmniAI: AI usage has reached 70% of the monthly limit", message.Subject);
        Assert.Contains("https://app.omniai.example/plan-usage", message.TextBody, StringComparison.Ordinal);

        NotificationEmail email = Assert.Single(await harness.EmailsAsync(world.Tenant));
        Assert.Equal(NotificationEmailStatus.Sent, email.Status);
        Assert.NotNull(email.SentAt);

        // Nothing left to claim: a sent email is never sent again.
        Assert.Empty(await harness.SendAllEmailsAsync());
        Assert.Single(harness.Email.Sent);
    }

    [Fact]
    public async Task Losing_the_permission_before_sending_skips_the_email()
    {
        (ReportWorld world, NotificationHarness harness, UserId member) = await QueuedForMemberAsync(PlatformPermissions.AnalyticsView);

        await world.RevokeAsync(member);

        Assert.Equal([NotificationEmailStatus.Skipped], await harness.SendAllEmailsAsync());
        Assert.Empty(harness.Email.Sent);
        Assert.Equal(NotificationEmailFailures.PermissionRevoked, Assert.Single(await harness.EmailsAsync(world.Tenant)).FailureCode);

        // The in-app notification is untouched by what became of its email.
        Assert.Single(await harness.NotificationsAsync(world.Tenant, member));
    }

    [Fact]
    public async Task Leaving_the_workspace_before_sending_skips_the_email()
    {
        (ReportWorld world, NotificationHarness harness, UserId member) = await QueuedForMemberAsync(PlatformPermissions.AnalyticsView);

        await using (OmniAiDbContext context = database.CreateContext())
        {
            await context.TenantRoleAssignments.Where(a => a.TenantId == world.Tenant && a.UserId == member).ExecuteDeleteAsync();
            await context.TenantMemberships.Where(m => m.TenantId == world.Tenant && m.UserId == member).ExecuteDeleteAsync();
        }

        Assert.Equal([NotificationEmailStatus.Skipped], await harness.SendAllEmailsAsync());
        Assert.Empty(harness.Email.Sent);
        Assert.Equal(NotificationEmailFailures.NotAMember, Assert.Single(await harness.EmailsAsync(world.Tenant)).FailureCode);
    }

    [Fact]
    public async Task Turning_the_category_off_before_sending_skips_the_email()
    {
        (ReportWorld world, NotificationHarness harness, UserId member) = await QueuedForMemberAsync(PlatformPermissions.AnalyticsView);

        await harness.EmailEverythingAsync(world.Tenant, member, on: false);

        Assert.Equal([NotificationEmailStatus.Skipped], await harness.SendAllEmailsAsync());
        Assert.Equal(NotificationEmailFailures.PreferenceDisabled, Assert.Single(await harness.EmailsAsync(world.Tenant)).FailureCode);
    }

    [Fact]
    public async Task An_ambiguous_send_is_unknown_and_never_retried()
    {
        (ReportWorld world, NotificationHarness harness) = await QueuedAsync(PlatformPermissions.AnalyticsView);

        harness.Email.Next = new OperationalEmailResult(OperationalEmailOutcome.Ambiguous, NotificationEmailFailures.Ambiguous);

        Assert.Equal([NotificationEmailStatus.Unknown], await harness.SendAllEmailsAsync());

        harness.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Empty(await harness.SendAllEmailsAsync());
        Assert.Single(harness.Email.Sent);
        Assert.Equal(NotificationEmailStatus.Unknown, Assert.Single(await harness.EmailsAsync(world.Tenant)).Status);
    }

    [Fact]
    public async Task A_send_that_certainly_failed_is_retried_a_bounded_number_of_times()
    {
        (ReportWorld world, NotificationHarness harness) = await QueuedAsync(PlatformPermissions.AnalyticsView);

        harness.Email.Next = new OperationalEmailResult(OperationalEmailOutcome.NotSent, NotificationEmailFailures.ConnectFailed);

        Assert.Equal([NotificationEmailStatus.Pending], await harness.SendAllEmailsAsync(maxAttempts: 2));

        // Backed off: not claimable again straight away.
        Assert.Empty(await harness.SendAllEmailsAsync(maxAttempts: 2));

        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal([NotificationEmailStatus.Failed], await harness.SendAllEmailsAsync(maxAttempts: 2));

        NotificationEmail email = Assert.Single(await harness.EmailsAsync(world.Tenant));
        Assert.Equal(NotificationEmailFailures.AttemptsExhausted, email.FailureCode);
        Assert.Equal(2, email.Attempts);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(await harness.SendAllEmailsAsync(maxAttempts: 2));
    }

    [Fact]
    public async Task Two_instances_claim_disjoint_emails_and_a_stale_claim_settles_nothing()
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        NotificationHarness harness = new(database);

        await harness.EmailEverythingAsync(world.Tenant, world.Owner);

        for (int month = 1; month <= 6; month++)
        {
            await harness.RecordAsync(world.Tenant, NotificationKind.UsageWarning, string.Create(CultureInfo.InvariantCulture, $"2026-{month:00}"));
        }

        await harness.DispatchAllAsync();

        DateTimeOffset now = harness.Clock.GetUtcNow();

        IReadOnlyList<ClaimedEmail>[] claims = await Task.WhenAll(ClaimAsync(now), ClaimAsync(now));

        List<Guid> ids = [.. claims.SelectMany(claim => claim).Select(claim => claim.Id.Value)];

        Assert.Equal(6, ids.Count);
        Assert.Equal(6, ids.Distinct().Count());

        // A worker whose claim was taken over cannot overwrite the outcome.
        ClaimedEmail anyone = claims.SelectMany(claim => claim).First();

        await using OmniAiDbContext context = database.CreateContext();
        NotificationEmailOutbox outbox = new(context);

        Assert.False(await outbox.SettleAsync(anyone.Id, Guid.NewGuid(), NotificationEmailStatus.Sent, null, now));
        Assert.True(await outbox.SettleAsync(anyone.Id, anyone.ClaimToken, NotificationEmailStatus.Sent, null, now));

        // A crashed worker's Sending rows become Unknown, never Pending again.
        Assert.Equal(5, await outbox.AbandonStaleAsync(now.AddMinutes(1), now.AddMinutes(1)));
        Assert.All(
            (await harness.EmailsAsync(world.Tenant)).Where(email => email.Id != anyone.Id),
            email => Assert.Equal(NotificationEmailStatus.Unknown, email.Status));
    }

    // ------------------------------------------------------ SMTP, for real

    [Fact]
    public async Task A_real_email_reaches_a_loopback_sink_with_both_parts_escaped_content_and_no_attachment()
    {
        await using SmtpSink sink = new();

        ReportWorld world = await ReportWorld.CreateAsync(database, "<script>alert(1)</script> <img src=x onerror=alert(1)>");
        NotificationHarness harness = new(database);

        OperationalEmailOptions options = Loopback(sink.Port);
        IOperationalEmailSender sender = NotificationsServiceCollectionExtensions.SenderFor(options, Configuration(), requireLiveEmailOptIn: true);

        // A loopback sink needs no opt-in, even in Development.
        Assert.IsType<SmtpOperationalEmailSender>(sender);

        await harness.EmailEverythingAsync(world.Tenant, world.Owner);
        await harness.RecordAsync(world.Tenant, NotificationKind.BillingPastDue, "sub:PastDue:smtp");

        await using (OmniAiDbContext context = database.CreateContext())
        {
            NotificationDirectory directory = new(context);

            await new NotificationDispatcher(
                    new NotificationOccurrenceStore(context),
                    directory,
                    directory,
                    NotificationHarness.Authority(context),
                    new NotificationStore(context),
                    sender,
                    harness.Realtime,
                    harness.Options,
                    harness.Clock)
                .DispatchAsync(Assert.Single(await new NotificationOccurrenceStore(context).ClaimAsync(10, 5, TimeSpan.FromMinutes(5), harness.Clock.GetUtcNow())));
        }

        ClaimedEmail claimed;

        await using (OmniAiDbContext context = database.CreateContext())
        {
            claimed = Assert.Single(await new NotificationEmailOutbox(context).ClaimAsync(10, 3, harness.Clock.GetUtcNow()));
        }

        await using (OmniAiDbContext context = database.CreateContext())
        {
            OperationalEmailDispatcher dispatcher = new(
                new NotificationEmailOutbox(context),
                new NotificationStore(context),
                NotificationHarness.Authority(context),
                new OmniAI.Infrastructure.Identity.IdentityAccountDirectory(context),
                new OmniAI.Infrastructure.Persistence.Repositories.TenantRepository(context),
                sender,
                new OperationalEmailLinks(new Uri("http://localhost:4200/")),
                harness.Clock);

            Assert.Equal(NotificationEmailStatus.Sent, await dispatcher.SendAsync(claimed, 3));
        }

        SinkMessage message = Assert.Single(sink.Messages);

        // Addressed to the member's own account and nobody else.
        string recipient = Assert.Single(message.Recipients);
        Assert.Contains("@example.test", recipient, StringComparison.Ordinal);
        Assert.Contains("notifications@omniai.example", message.MailFrom, StringComparison.Ordinal);

        Assert.Contains("Subject: OmniAI: Subscription payment is past due", message.Headers, StringComparison.Ordinal);
        Assert.Contains("Auto-Submitted: auto-generated", message.Headers, StringComparison.Ordinal);
        Assert.DoesNotContain("Bcc:", message.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cc:", message.Headers, StringComparison.Ordinal);

        Assert.Contains("multipart/alternative", message.Raw, StringComparison.Ordinal);
        Assert.Contains("text/plain", message.Raw, StringComparison.Ordinal);
        Assert.Contains("text/html", message.Raw, StringComparison.Ordinal);
        Assert.DoesNotContain("attachment", message.Raw, StringComparison.OrdinalIgnoreCase);

        // The workspace name arrives as text in the HTML part: no tag survives.
        // (The text/plain part carries it literally, which is correct — plain text
        // is never rendered as markup.)
        string decoded = Decode(message.Raw);
        string html = decoded[decoded.IndexOf("Content-Type: text/html", StringComparison.Ordinal)..];

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);

        // The only link is the trusted application address, with no token in it.
        Assert.Contains("http://localhost:4200/billing", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("token", decoded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_line_break_in_a_label_never_becomes_a_header()
    {
        await using SmtpSink sink = new();

        Notification notification = Notification.Create(
            Domain.Tenants.TenantId.From(Guid.NewGuid()),
            UserId.New(),
            NotificationOccurrenceId.New(),
            NotificationKind.ReportCompleted,
            "Your report is ready",
            "Body",
            "/reports",
            "k",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(1));

        OperationalEmailMessage message = OperationalEmailRenderer.Render(
            notification,
            EmailAddress.Create("member@example.test"),
            "Header\r\nBcc: attacker@example.invalid\r\nSubject: hijacked",
            new OperationalEmailLinks(new Uri("http://localhost:4200/")));

        OperationalEmailResult result = await new SmtpOperationalEmailSender(Loopback(sink.Port)).SendAsync(message);

        Assert.Equal(OperationalEmailOutcome.Sent, result.Outcome);

        SinkMessage sent = Assert.Single(sink.Messages);

        Assert.Single(sent.Recipients);
        Assert.DoesNotContain("attacker", string.Join(" ", sent.Recipients), StringComparison.Ordinal);
        Assert.DoesNotContain("Bcc:", sent.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hijacked", sent.Headers, StringComparison.Ordinal);
        Assert.Contains("Subject: OmniAI: Your report is ready", sent.Headers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_recipient_is_rejected_and_a_dropped_answer_is_ambiguous()
    {
        OperationalEmailMessage message = Message();

        await using (SmtpSink rejecting = new(SmtpSinkMode.RejectRecipient))
        {
            OperationalEmailResult rejected = await new SmtpOperationalEmailSender(Loopback(rejecting.Port)).SendAsync(message);

            Assert.Equal(OperationalEmailOutcome.Rejected, rejected.Outcome);
        }

        await using (SmtpSink dropping = new(SmtpSinkMode.DropAfterData))
        {
            OperationalEmailResult dropped = await new SmtpOperationalEmailSender(Loopback(dropping.Port)).SendAsync(message);

            Assert.Equal(OperationalEmailOutcome.Ambiguous, dropped.Outcome);
        }
    }

    [Fact]
    public async Task Nothing_listening_is_a_certain_not_sent()
    {
        int port;

        await using (SmtpSink closed = new())
        {
            port = closed.Port;
        }

        OperationalEmailResult result = await new SmtpOperationalEmailSender(Loopback(port)).SendAsync(Message());

        Assert.Equal(OperationalEmailOutcome.NotSent, result.Outcome);
        Assert.Equal(NotificationEmailFailures.ConnectFailed, result.FailureCode);
    }

    // ------------------------------------------------- Development guard

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("yes")]
    public async Task Development_withholds_a_non_loopback_host_without_the_exact_opt_in(string? optIn)
    {
        OperationalEmailOptions options = External();

        IOperationalEmailSender sender = NotificationsServiceCollectionExtensions.SenderFor(
            options, Configuration(optIn), requireLiveEmailOptIn: true);

        // The withheld sender has no network code at all: zero connections by construction.
        Assert.IsType<WithheldOperationalEmailSender>(sender);

        OperationalEmailResult result = await sender.SendAsync(Message());

        Assert.Equal(NotificationEmailFailures.LiveEmailDisabled, result.FailureCode);
        Assert.NotEqual(OperationalEmailOutcome.Sent, result.Outcome);
    }

    [Fact]
    public async Task Development_with_a_loopback_sink_sends_without_the_opt_in()
    {
        await using SmtpSink sink = new();

        IOperationalEmailSender sender = NotificationsServiceCollectionExtensions.SenderFor(
            Loopback(sink.Port), Configuration(), requireLiveEmailOptIn: true);

        Assert.Equal(OperationalEmailOutcome.Sent, (await sender.SendAsync(Message())).Outcome);
        Assert.Equal(1, sink.Connections);
        Assert.Single(sink.Messages);
    }

    [Fact]
    public void Production_does_not_need_the_development_opt_in()
    {
        IOperationalEmailSender sender = NotificationsServiceCollectionExtensions.SenderFor(
            External(), Configuration(), requireLiveEmailOptIn: false);

        Assert.IsType<SmtpOperationalEmailSender>(sender);
    }

    [Fact]
    public void Development_with_the_exact_opt_in_uses_the_real_transport()
    {
        IOperationalEmailSender sender = NotificationsServiceCollectionExtensions.SenderFor(
            External(), Configuration("1"), requireLiveEmailOptIn: true);

        Assert.IsType<SmtpOperationalEmailSender>(sender);
    }

    [Fact]
    public void No_host_means_no_email_at_all()
    {
        IOperationalEmailSender sender = NotificationsServiceCollectionExtensions.SenderFor(
            new OperationalEmailOptions(), Configuration(), requireLiveEmailOptIn: false);

        Assert.False(sender.IsConfigured);
    }

    [Theory]
    [InlineData("smtp.example.invalid", "None", "notifications@omniai.example", "https://app.omniai.example", null, null)]
    [InlineData("smtp.example.invalid", "StartTls", "notifications@omniai.example\r\nBcc: x@example.invalid", "https://app.omniai.example", null, null)]
    [InlineData("smtp.example.invalid", "StartTls", "not-an-address", "https://app.omniai.example", null, null)]
    [InlineData("smtp.example.invalid", "StartTls", "notifications@omniai.example", "http://app.omniai.example", null, null)]
    [InlineData("smtp.example.invalid", "StartTls", "notifications@omniai.example", "https://user:pw@app.omniai.example", null, null)]
    [InlineData("smtp.example.invalid", "StartTls", "notifications@omniai.example", "https://app.omniai.example/?next=https://evil.example", null, null)]
    [InlineData("smtp.example.invalid", "StartTls", "notifications@omniai.example", "https://app.omniai.example", "user", null)]
    [InlineData("smtp.example.invalid", "StartTls", "notifications@omniai.example", null, null, null)]
    public void Unsafe_email_configuration_refuses_to_start(
        string host,
        string security,
        string from,
        string? appBaseUrl,
        string? username,
        string? password)
    {
        OperationalEmailOptions options = new()
        {
            AppBaseUrl = appBaseUrl,
            Smtp = new SmtpSettings
            {
                Host = host,
                Security = Enum.Parse<SmtpSecurity>(security),
                FromAddress = from,
                Username = username,
                Password = password,
            },
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    // ----------------------------------------------------------------- helpers

    private async Task<(ReportWorld World, NotificationHarness Harness)> QueuedAsync(PermissionCode permission)
    {
        (ReportWorld world, NotificationHarness harness, _) = await QueuedForMemberAsync(permission, ownerToo: true);

        return (world, harness);
    }

    /// <summary>A usage alert queued for one member (and, optionally, the Owner) with email on.</summary>
    private async Task<(ReportWorld World, NotificationHarness Harness, UserId Member)> QueuedForMemberAsync(
        PermissionCode permission,
        bool ownerToo = false)
    {
        ReportWorld world = await ReportWorld.CreateAsync(database);
        UserId member = await world.MemberAsync(permission);
        NotificationHarness harness = new(database);

        await harness.EmailEverythingAsync(world.Tenant, ownerToo ? world.Owner : member);
        await harness.RecordAsync(world.Tenant, NotificationKind.UsageWarning, "2026-09");
        await harness.DispatchAllAsync();

        Assert.Single(await harness.EmailsAsync(world.Tenant));

        return (world, harness, ownerToo ? world.Owner : member);
    }

    private async Task<IReadOnlyList<ClaimedEmail>> ClaimAsync(DateTimeOffset now)
    {
        await using OmniAiDbContext context = database.CreateContext();

        return await new NotificationEmailOutbox(context).ClaimAsync(3, 3, now);
    }

    private static OperationalEmailOptions Loopback(int port)
    {
        OperationalEmailOptions options = new()
        {
            AppBaseUrl = "http://localhost:4200",
            Smtp = new SmtpSettings
            {
                Host = "127.0.0.1",
                Port = port,
                Security = SmtpSecurity.None,
                FromAddress = "notifications@omniai.example",
                Timeout = TimeSpan.FromSeconds(5),
            },
        };

        options.Validate();

        return options;
    }

    private static OperationalEmailOptions External()
    {
        OperationalEmailOptions options = new()
        {
            AppBaseUrl = "https://app.omniai.example",
            Smtp = new SmtpSettings
            {
                Host = "smtp.example.invalid",
                Port = 587,
                Security = SmtpSecurity.StartTls,
                FromAddress = "notifications@omniai.example",
                Username = "synthetic-user",
                Password = "synthetic-not-a-secret",
            },
        };

        options.Validate();

        return options;
    }

    private static IConfiguration Configuration(string? optIn = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [OperationalEmailOptions.LiveEmailOptInVariable] = optIn,
            })
            .Build();

    private static OperationalEmailMessage Message() =>
        new(EmailAddress.Create("member@example.test"), "OmniAI: test", "text", "<p>html</p>");

    /// <summary>The message with quoted-printable soft breaks and escapes undone, for searching.</summary>
    private static string Decode(string raw)
    {
        string joined = raw.Replace("=\r\n", string.Empty, StringComparison.Ordinal);

        return System.Text.RegularExpressions.Regex.Replace(
            joined,
            "=([0-9A-F]{2})",
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
    }
}
