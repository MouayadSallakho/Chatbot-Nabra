using OmniAI.Application.Notifications;
using OmniAI.Application.Reports;
using OmniAI.Domain.Ai;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Reports;

/// <summary>
/// The words a report file says, in English and Arabic (Phase 37.7).
/// </summary>
public sealed class ReportWordingTests
{
    private static readonly ReportPeriod October = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1));

    [Fact]
    public void English_returns_every_phrase_untouched()
    {
        foreach (string phrase in ReportWording.TranslatedPhrases)
        {
            Assert.Same(phrase, ReportWording.English[phrase]);
        }

        Assert.Equal("Something not in the catalogue", ReportWording.English["Something not in the catalogue"]);
    }

    [Fact]
    public void Every_phrase_has_arabic_and_the_arabic_is_arabic()
    {
        Assert.NotEmpty(ReportWording.TranslatedPhrases);

        foreach (string phrase in ReportWording.TranslatedPhrases)
        {
            string arabic = ReportWording.Arabic[phrase];

            Assert.NotEqual(phrase, arabic);
            Assert.True(HasArabic(arabic), $"«{arabic}» for «{phrase}» has no Arabic letter.");

            // Arabic punctuation, never the Latin comma or semicolon inside a phrase.
            Assert.DoesNotContain(',', arabic);
            Assert.DoesNotContain(';', arabic);

            // Nothing a reader sees names the code name.
            Assert.DoesNotContain("omniai", arabic, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Report_names_are_the_names_report_emails_use()
    {
        foreach (ReportType type in Enum.GetValues<ReportType>())
        {
            Assert.Equal(ReportCatalog.For(type).Title, ReportWording.English.Title(type));
            Assert.Equal(NotificationCatalog.ReportName(type, UserLanguage.Arabic), ReportWording.Arabic.Title(type));
            Assert.True(HasArabic(ReportWording.Arabic.Title(type)));
        }
    }

    [Fact]
    public void Glossary_terms_are_used()
    {
        ReportWording ar = ReportWording.Arabic;

        Assert.Equal("دردشة الموقع", ar.Channel(ChannelKind.WebChat));
        Assert.Equal("واتساب", ar.Channel(ChannelKind.WhatsApp));
        Assert.Equal("إنستجرام", ar.Channel(ChannelKind.Instagram));
        Assert.Equal("ماسنجر", ar.Channel(ChannelKind.Messenger));
        Assert.Equal("مساحة العمل", ar["Workspace"]);
        Assert.Contains("وكيل", ar["AI agent"], StringComparison.Ordinal);
        Assert.Contains("محادث", ar["Conversations started"], StringComparison.Ordinal);
        Assert.Equal("دردشة الموقع، واتساب", ar.Channels([ChannelKind.WebChat, ChannelKind.WhatsApp]));
        Assert.Equal("Website, WhatsApp", ReportWording.English.Channels([ChannelKind.WebChat, ChannelKind.WhatsApp]));
        Assert.Equal("توليد النصوص", ar.Operation(AiOperationType.TextGeneration));

        // Every channel the platform knows has an Arabic name.
        foreach (ChannelKind channel in Enum.GetValues<ChannelKind>())
        {
            Assert.True(HasArabic(ar.Channel(channel)), channel.ToString());
        }
    }

    [Fact]
    public void Zones_digits_and_dates_follow_localisation_decision_3()
    {
        ReportWording ar = ReportWording.Arabic;
        WorkspaceTimeZone riyadh = WorkspaceTimeZone.Create("Asia/Riyadh");

        Assert.Equal("التاريخ (بالتوقيت العالمي)", ar.DateHeader(WorkspaceTimeZone.Utc));
        Assert.Equal("التاريخ (بتوقيت Asia/Riyadh)", ar.DateHeader(riyadh));
        Assert.Equal("Date (Asia/Riyadh)", ReportWording.English.DateHeader(riyadh));

        // Western digits and ISO dates in both languages.
        // Each ISO date inside the Arabic sentence is wrapped in left-to-right marks,
        // or the bidirectional algorithm would show 2026-10-01 as 01-10-2026.
        string lrm = ((char)0x200E).ToString();
        Assert.Equal(
            $"الفترة: من {lrm}2026-10-01{lrm} إلى {lrm}2026-10-31{lrm} شاملةً، بأيام كاملة بالتوقيت العالمي.",
            ar.PeriodNote(October));
        Assert.Equal($"من {lrm}2026-10-01{lrm} إلى {lrm}2026-10-31{lrm} شاملةً", ar.PeriodRange(October));
        Assert.Equal("Period: 2026-10-01 to 2026-10-31 inclusive, in UTC whole days.", ReportWording.English.PeriodNote(October));
        Assert.Equal("صفحة 2 من 12", ar.PageOf(2, 12));
        Assert.Equal("Page 2 of 12", ReportWording.English.PageOf(2, 12));

        foreach (char digit in "٠١٢٣٤٥٦٧٨٩")
        {
            Assert.DoesNotContain(digit, ar.PeriodNote(October) + ar.PageOf(2, 12) + ar.PeriodRange(October));
        }
    }

    [Fact]
    public void A_customer_nobody_named_gets_the_screens_label_and_a_named_one_keeps_their_name()
    {
        ReportWording ar = ReportWording.Arabic;

        Assert.Equal("عميل واتساب", ar.Customer(null, [ChannelKind.WhatsApp]));
        Assert.Equal("زائر مجهول", ar.Customer(null, [ChannelKind.WebChat]));
        Assert.Equal("عميل بلا اسم", ar.Customer(null, [ChannelKind.WebChat, ChannelKind.WhatsApp]));
        Assert.Equal("José García", ar.Customer(CustomerDisplayName.Create("José García"), [ChannelKind.WebChat]));
        Assert.Equal("Anonymous visitor", ReportWording.English.Customer(null, [ChannelKind.WebChat]));
    }

    [Fact]
    public void Only_arabic_reads_right_to_left()
    {
        Assert.True(ReportWording.Arabic.IsRightToLeft);
        Assert.False(ReportWording.English.IsRightToLeft);
        Assert.Same(ReportWording.Arabic, ReportWording.For(UserLanguage.Arabic));
        Assert.Same(ReportWording.English, ReportWording.For(UserLanguage.English));
    }

    internal static bool HasArabic(string text) => text.Any(character => character is >= '\u0600' and <= '\u06FF');
}
