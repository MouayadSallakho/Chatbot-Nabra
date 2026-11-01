using OmniAI.Application.Accounts;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Accounts;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Notifications;

/// <summary>
/// Every email Nabra sends, in English and in Arabic (Phase 37.3, row 8).
/// </summary>
public sealed class BilingualEmailTests
{
    private static readonly OperationalEmailLinks Links = new(new Uri("https://app.nabra.example/"));

    private static readonly EmailAddress To = EmailAddress.Create("member@example.test");

    public static TheoryData<NotificationKind, UserLanguage> KindsAndLanguages()
    {
        TheoryData<NotificationKind, UserLanguage> data = [];

        foreach (NotificationKind kind in Enum.GetValues<NotificationKind>())
        {
            data.Add(kind, UserLanguage.English);
            data.Add(kind, UserLanguage.Arabic);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KindsAndLanguages))]
    public void Every_operational_kind_has_its_words_in_both_languages(NotificationKind kind, UserLanguage language)
    {
        NotificationText text = NotificationCatalog.Describe(kind, language, ReportType.ChannelUsage);

        Assert.InRange(text.Title.Length, 1, Notification.MaxTitleLength);
        Assert.InRange(text.Body.Length, 1, Notification.MaxBodyLength);
        Assert.Equal(NotificationCatalog.Describe(kind, ReportType.ChannelUsage).ActionPath, text.ActionPath);
        Assert.Equal(language == UserLanguage.Arabic, HasArabic(text.Title));
        Assert.Equal(language == UserLanguage.Arabic, HasArabic(text.Body));

        // A constant line per kind and language: nothing can reach a header.
        string subject = NotificationCatalog.Subject(kind, language);
        Assert.StartsWith(language == UserLanguage.Arabic ? "نبرة: " : "Nabra: ", subject, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain(EmailWords.FirstStrongIsolate, subject);
    }

    [Theory]
    [MemberData(nameof(KindsAndLanguages))]
    public void Every_operational_email_is_laid_out_for_its_language_and_loads_nothing(NotificationKind kind, UserLanguage language)
    {
        OperationalEmailMessage email = OperationalEmailRenderer.Render(
            NotificationOf(kind), To, "Northwind", Links, language, ReportType.AgentActivity);

        bool arabic = language == UserLanguage.Arabic;

        Assert.Contains(arabic ? "<html lang=\"ar\" dir=\"rtl\">" : "<html lang=\"en\" dir=\"ltr\">", email.HtmlBody, StringComparison.Ordinal);
        Assert.StartsWith(arabic ? "نبرة\n" : "Nabra\n", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("<bdi>Northwind</bdi>", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("\u2068Northwind\u2069", email.TextBody, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(email.HtmlBody, "href="));
        Assert.DoesNotContain("<img", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("omniai", email.Subject + email.TextBody + email.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(arabic, HasArabic(email.TextBody));
    }

    [Theory]
    [InlineData(UserLanguage.English, "Your “\u2068AI agent activity\u2069” report has finished generating and is ready to download.")]
    [InlineData(UserLanguage.Arabic, "اكتمل إنشاء تقريرك «\u2068نشاط وكلاء الذكاء الاصطناعي\u2069» وهو جاهز للتنزيل.")]
    public void A_report_email_names_the_report_in_its_language_isolated(UserLanguage language, string body)
    {
        NotificationText text = NotificationCatalog.Describe(NotificationKind.ReportCompleted, language, ReportType.AgentActivity);

        Assert.Equal(body, text.Body);
    }

    [Fact]
    public void A_report_whose_type_is_gone_is_described_without_a_name()
    {
        Assert.Equal(
            "اكتمل إنشاء تقريرك وهو جاهز للتنزيل.",
            NotificationCatalog.Describe(NotificationKind.ReportCompleted, UserLanguage.Arabic).Body);
        Assert.Equal(
            "Your report has finished generating and is ready to download.",
            NotificationCatalog.Describe(NotificationKind.ReportCompleted, UserLanguage.English).Body);
    }

    [Fact]
    public void Every_report_type_has_an_arabic_name()
    {
        foreach (ReportType type in Enum.GetValues<ReportType>())
        {
            Assert.True(HasArabic(NotificationCatalog.ReportName(type, UserLanguage.Arabic)), type.ToString());
        }
    }

    [Fact]
    public void A_workspace_name_cannot_smuggle_its_own_isolates_or_markup()
    {
        OperationalEmailMessage email = OperationalEmailRenderer.Render(
            NotificationOf(NotificationKind.BillingPastDue),
            To,
            "\u2069<b>Evil</b>\u2066",
            Links,
            UserLanguage.Arabic,
            null);

        Assert.Contains("<bdi>&lt;b&gt;Evil&lt;/b&gt;</bdi>", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", email.HtmlBody, StringComparison.Ordinal);
    }

    public static TheoryData<AccountEmailKind, UserLanguage> AccountKindsAndLanguages()
    {
        TheoryData<AccountEmailKind, UserLanguage> data = [];

        foreach (AccountEmailKind kind in Enum.GetValues<AccountEmailKind>())
        {
            data.Add(kind, UserLanguage.English);
            data.Add(kind, UserLanguage.Arabic);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AccountKindsAndLanguages))]
    public void Every_account_email_has_both_languages_one_link_and_a_constant_subject(AccountEmailKind kind, UserLanguage language)
    {
        Uri link = Links.WithFragment("/reset-password", "uid=1&token=abc");

        OperationalEmailMessage email = kind switch
        {
            AccountEmailKind.PasswordReset => AccountEmailRenderer.PasswordReset(To, language, link, TimeSpan.FromMinutes(30)),
            AccountEmailKind.PasswordChanged => AccountEmailRenderer.PasswordChanged(To, language, Links.For("/sign-in")),
            _ => AccountEmailRenderer.Invitation(To, language, "Northwind", new DateTimeOffset(2026, 10, 28, 9, 5, 0, TimeSpan.Zero), link),
        };

        bool arabic = language == UserLanguage.Arabic;

        Assert.Equal(AccountEmailRenderer.Subject(kind, language), email.Subject);
        Assert.StartsWith(arabic ? "نبرة: " : "Nabra: ", email.Subject, StringComparison.Ordinal);
        Assert.Contains(arabic ? "dir=\"rtl\"" : "dir=\"ltr\"", email.HtmlBody, StringComparison.Ordinal);
        Assert.Equal(arabic, HasArabic(email.TextBody));
        Assert.Equal(1, Occurrences(email.HtmlBody, "href="));
        Assert.DoesNotContain("omniai", email.Subject + email.TextBody + email.HtmlBody, StringComparison.OrdinalIgnoreCase);

        if (kind == AccountEmailKind.PasswordChanged)
        {
            Assert.DoesNotContain("token", email.TextBody, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("#uid=1&token=abc", email.TextBody, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(1, "دقيقة واحدة", "1 minute")]
    [InlineData(2, "دقيقتين", "2 minutes")]
    [InlineData(10, "10 دقائق", "10 minutes")]
    [InlineData(30, "30 دقيقة", "30 minutes")]
    public void The_reset_email_says_how_long_the_link_works(int minutes, string arabic, string english)
    {
        Uri link = Links.WithFragment("/reset-password", "uid=1&token=abc");

        Assert.Contains(arabic, AccountEmailRenderer.PasswordReset(To, UserLanguage.Arabic, link, TimeSpan.FromMinutes(minutes)).TextBody, StringComparison.Ordinal);
        Assert.Contains(english, AccountEmailRenderer.PasswordReset(To, UserLanguage.English, link, TimeSpan.FromMinutes(minutes)).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invitation_email_isolates_the_workspace_and_the_expiry()
    {
        OperationalEmailMessage email = AccountEmailRenderer.Invitation(
            To,
            UserLanguage.Arabic,
            "<script>x</script>",
            new DateTimeOffset(2026, 10, 28, 9, 5, 0, TimeSpan.Zero),
            Links.WithFragment("/accept-invitation", "token=abc"));

        Assert.Contains("<bdi>&lt;script&gt;x&lt;/script&gt;</bdi>", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("<bdi>2026-10-28 09:05</bdi>", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("https://app.nabra.example/accept-invitation#token=abc", email.TextBody, StringComparison.Ordinal);
    }

    private static bool HasArabic(string text) => text.Any(character => character is >= '؀' and <= 'ۿ');

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
            TimeSpan.FromDays(1));
    }
}
