using OmniAI.Application.Notifications;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Notifications;

/// <summary>
/// The fixed words and links a notification or email can carry.
/// </summary>
public sealed class NotificationTemplateTests
{
    private static readonly OperationalEmailLinks Links = new(new Uri("https://app.omniai.example/"));

    public static TheoryData<NotificationKind> Kinds()
    {
        TheoryData<NotificationKind> kinds = [];

        foreach (NotificationKind kind in Enum.GetValues<NotificationKind>())
        {
            kinds.Add(kind);
        }

        return kinds;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_has_bounded_text_and_an_internal_action(NotificationKind kind)
    {
        NotificationText text = NotificationCatalog.Describe(
            kind, ReportType.CustomerActivity, Guid.NewGuid(), Guid.NewGuid());

        Assert.InRange(text.Title.Length, 1, Notification.MaxTitleLength);
        Assert.InRange(text.Body.Length, 1, Notification.MaxBodyLength);
        Assert.True(NotificationActionPath.IsValid(text.ActionPath), text.ActionPath);

        // The subject is a constant line: no line break can reach a header.
        string subject = NotificationCatalog.Subject(kind);
        Assert.StartsWith("Nabra: ", subject, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_email_names_Nabra_and_never_the_code_name(NotificationKind kind)
    {
        // A link base without the code name in it, so that what is asserted is
        // the product's own words rather than a deployment's address.
        OperationalEmailMessage email = OperationalEmailRenderer.Render(
            NotificationOf(kind),
            EmailAddress.Create("member@example.test"),
            "Northwind",
            new OperationalEmailLinks(new Uri("https://app.nabra.example/")));

        Assert.StartsWith("Nabra: ", email.Subject, StringComparison.Ordinal);
        Assert.StartsWith("Nabra\n", email.TextBody, StringComparison.Ordinal);
        Assert.Contains(">Nabra</p>", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("your Nabra notification preferences", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("your Nabra notification preferences", email.HtmlBody, StringComparison.Ordinal);

        Assert.DoesNotContain("omniai", email.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("omniai", email.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("omniai", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(NotificationCategory.Reports, "reports.view")]
    [InlineData(NotificationCategory.Usage, "analytics.view")]
    [InlineData(NotificationCategory.Billing, "billing.view")]
    [InlineData(NotificationCategory.Knowledge, "knowledge.manage")]
    [InlineData(NotificationCategory.ChannelHealth, "whatsapp.manage")]
    public void Recipients_are_chosen_by_existing_permissions(NotificationCategory category, string permission) =>
        Assert.Equal(permission, NotificationCatalog.RequiredPermission(category).Value);

    [Fact]
    public void Report_text_names_the_catalogue_title_only()
    {
        NotificationText text = NotificationCatalog.Describe(NotificationKind.ReportCompleted, ReportType.AgentActivity);

        Assert.Contains("“AI agent activity”", text.Body, StringComparison.Ordinal);
        Assert.Equal("/reports", text.ActionPath);
    }

    [Fact]
    public void An_email_escapes_every_workspace_authored_value_and_loads_nothing()
    {
        OperationalEmailMessage email = OperationalEmailRenderer.Render(
            NotificationOf(NotificationKind.ReportCompleted),
            EmailAddress.Create("member@example.test"),
            "<script>alert(1)</script><img src=x onerror=alert(1)>",
            Links);

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", email.HtmlBody, StringComparison.OrdinalIgnoreCase);

        // No remote resource of any kind: nothing to load, nothing to track.
        Assert.DoesNotContain("<link", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Occurrences(email.HtmlBody, "href="));

        Assert.Equal("Nabra: Your report is ready", email.Subject);
        Assert.Contains("https://app.omniai.example/reports", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.omniai.example/reports\"", email.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_break_in_a_label_stays_in_the_body_as_a_space()
    {
        OperationalEmailMessage email = OperationalEmailRenderer.Render(
            NotificationOf(NotificationKind.BillingPastDue),
            EmailAddress.Create("member@example.test"),
            "Header\r\nBcc: attacker@example.invalid",
            Links);

        Assert.Equal("Nabra: Subscription payment is past due", email.Subject);
        Assert.DoesNotContain("\r\nBcc:", email.TextBody, StringComparison.Ordinal);
        // Isolated since Phase 37.3 (FSI…PDI), so a name keeps its own direction.
        Assert.Contains("Workspace: \u2068Header  Bcc: attacker@example.invalid\u2069", email.TextBody, StringComparison.Ordinal);
        Assert.Equal("member@example.test", email.To.Value);
    }

    [Fact]
    public void Links_come_from_the_trusted_base_and_carry_no_query()
    {
        Assert.Equal("https://app.omniai.example/plan-usage", Links.For("/plan-usage").AbsoluteUri);
        Assert.Equal("https://app.omniai.example/", Links.For(null).AbsoluteUri);

        OperationalEmailLinks nested = new(new Uri("https://example.com/omniai/"));
        Assert.Equal("https://example.com/omniai/billing", nested.For("/billing").AbsoluteUri);
    }

    private static int Occurrences(string text, string value)
    {
        int count = 0;

        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static Notification NotificationOf(NotificationKind kind)
    {
        NotificationText text = NotificationCatalog.Describe(kind, ReportType.ChannelUsage);

        return Notification.Create(
            TenantId.From(Guid.NewGuid()),
            UserId.New(),
            NotificationOccurrenceId.New(),
            kind,
            text.Title,
            text.Body,
            text.ActionPath,
            "key",
            DateTimeOffset.UtcNow,
            TimeSpan.FromDays(90));
    }
}
