using OmniAI.Domain.Notifications;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Domain.Tests.Notifications;

public sealed class NotificationDomainTests
{
    private static readonly TenantId Tenant = TenantId.From(Guid.NewGuid());

    [Theory]
    [InlineData("/")]
    [InlineData("/reports")]
    [InlineData("/plan-usage")]
    [InlineData("/knowledge/0199c1a2-3b4c-7d5e-8f90-a1b2c3d4e5f6")]
    [InlineData("/channels/whatsapp/0199c1a2-3b4c-7d5e-8f90-a1b2c3d4e5f6")]
    public void Internal_routes_are_accepted(string path) => Assert.True(NotificationActionPath.IsValid(path));

    [Theory]
    [InlineData("")]
    [InlineData("reports")]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/javascript:alert(1)")]
    [InlineData("/reports?next=https://evil.example")]
    [InlineData("/reports#fragment")]
    [InlineData("/reports/../admin")]
    [InlineData("/reports//x")]
    [InlineData("/reports/")]
    [InlineData("/Reports")]
    [InlineData("/\\evil.example")]
    [InlineData("/%2F%2Fevil.example")]
    public void Anything_that_is_not_an_internal_route_is_refused(string path)
    {
        Assert.False(NotificationActionPath.IsValid(path));
        Assert.Throws<DomainValidationException>(() => Create(actionPath: path));
    }

    [Fact]
    public void Every_kind_has_exactly_one_category_and_severity()
    {
        foreach (NotificationKind kind in Enum.GetValues<NotificationKind>())
        {
            Assert.True(Enum.IsDefined(NotificationKinds.CategoryOf(kind)));
            Assert.True(Enum.IsDefined(NotificationKinds.SeverityOf(kind)));
        }
    }

    [Theory]
    [InlineData(NotificationKind.ReportCompleted, NotificationSeverity.Information)]
    [InlineData(NotificationKind.UsageWarning, NotificationSeverity.Warning)]
    [InlineData(NotificationKind.UsageCritical, NotificationSeverity.Critical)]
    [InlineData(NotificationKind.UsageExhausted, NotificationSeverity.Critical)]
    [InlineData(NotificationKind.BillingSuspended, NotificationSeverity.Critical)]
    [InlineData(NotificationKind.BillingRecovered, NotificationSeverity.Information)]
    [InlineData(NotificationKind.WhatsAppDeliveryUnknown, NotificationSeverity.Warning)]
    public void Severity_follows_the_documented_semantics(NotificationKind kind, NotificationSeverity severity) =>
        Assert.Equal(severity, NotificationKinds.SeverityOf(kind));

    [Fact]
    public void Reading_is_monotonic()
    {
        Notification notification = Create();
        DateTimeOffset first = DateTimeOffset.UtcNow;

        Assert.False(notification.IsRead);
        Assert.True(notification.MarkRead(first));
        Assert.False(notification.MarkRead(first.AddHours(1)));
        Assert.Equal(first, notification.ReadAt);
    }

    [Fact]
    public void A_notification_expires_after_its_retention()
    {
        DateTimeOffset now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

        Notification notification = Create(now: now, retention: TimeSpan.FromDays(90));

        Assert.Equal(now.AddDays(90), notification.ExpiresAt);
        Assert.Throws<DomainValidationException>(() => Create(retention: TimeSpan.Zero));
    }

    [Fact]
    public void Text_is_bounded()
    {
        Assert.Throws<DomainValidationException>(() => Create(title: new string('x', Notification.MaxTitleLength + 1)));
        Assert.Throws<DomainValidationException>(() => Create(body: " "));
    }

    [Fact]
    public void Email_is_off_for_every_category_until_chosen()
    {
        NotificationPreferences preferences = NotificationPreferences.Default(Tenant, UserId.New(), DateTimeOffset.UtcNow);

        foreach (NotificationCategory category in Enum.GetValues<NotificationCategory>())
        {
            Assert.False(preferences.EmailsFor(category));
        }

        preferences.Choose(reports: true, usage: false, billing: true, knowledge: false, channelHealth: false, DateTimeOffset.UtcNow);

        Assert.True(preferences.EmailsFor(NotificationCategory.Reports));
        Assert.True(preferences.EmailsFor(NotificationCategory.Billing));
        Assert.False(preferences.EmailsFor(NotificationCategory.Usage));
    }

    [Fact]
    public void An_occurrence_needs_a_bounded_key()
    {
        Assert.Throws<DomainValidationException>(() =>
            NotificationOccurrence.Record(Tenant, NotificationKind.UsageWarning, " ", null, DateTimeOffset.UtcNow));

        Assert.Throws<DomainValidationException>(() =>
            NotificationOccurrence.Record(Tenant, NotificationKind.UsageWarning, new string('k', NotificationOccurrence.MaxKeyLength + 1), null, DateTimeOffset.UtcNow));
    }

    private static Notification Create(
        string title = "Title",
        string body = "Body",
        string? actionPath = "/reports",
        DateTimeOffset? now = null,
        TimeSpan? retention = null) =>
        Notification.Create(
            Tenant,
            UserId.New(),
            NotificationOccurrenceId.New(),
            NotificationKind.ReportCompleted,
            title,
            body,
            actionPath,
            "ReportCompleted:key",
            now ?? DateTimeOffset.UtcNow,
            retention ?? TimeSpan.FromDays(90));
}
