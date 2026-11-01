using System.Globalization;
using OmniAI.Application.Reports;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>What a notification says and where it leads.</summary>
public sealed record NotificationText(string Title, string Body, string? ActionPath);

/// <summary>
/// Who is told about each kind, and in what words.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written in code, reviewed in a diff.</strong> Every sentence a member
/// can read, and every permission that decides whether they read it, is here. No
/// template is stored, edited by a workspace or assembled from a provider's text.
/// </para>
/// <para>
/// <strong>Only what the recipient could already see.</strong> Report kinds name
/// the report from the fixed catalogue. Nothing carries message text, knowledge
/// content, a document title, a phone number, a provider identifier, a payment
/// detail or an exception. A kind whose recipients hold the permission for that
/// area learns nothing there they could not read on that area's own screen.
/// </para>
/// <para>
/// <strong>Two languages for email (Phase 37.3).</strong> The in-app text is
/// stored in English, as before, and the client words it by kind. An email is
/// worded when it is sent, in the recipient's language, by
/// <see cref="Describe(NotificationKind, UserLanguage, ReportType?, Guid?, Guid?)"/>;
/// its sentences are the ones the client already shows, so a notification reads the
/// same in the inbox and in the bell. A report's name is wrapped in Unicode isolates
/// (FSI…PDI), so an English name inside Arabic text keeps its own direction.
/// </para>
/// <para>
/// <strong>Recipient policy uses existing permissions only.</strong>
/// Reports go to the member the report was generated for — the requester, or the
/// schedule's owner — and only while they hold <c>reports.view</c>. Usage goes to
/// holders of <c>analytics.view</c>, the permission the Plan &amp; usage screen
/// already requires. Billing goes to <c>billing.view</c>. Knowledge failures go to
/// <c>knowledge.manage</c>, the people who can index. WhatsApp delivery health goes
/// to <c>whatsapp.manage</c>, the channel's administrators.
/// </para>
/// </remarks>
public static class NotificationCatalog
{
    /// <summary>The permission a recipient must hold, now, to be told — and to be emailed.</summary>
    public static PermissionCode RequiredPermission(NotificationCategory category) => category switch
    {
        NotificationCategory.Reports => PlatformPermissions.ReportsView,
        NotificationCategory.Usage => PlatformPermissions.AnalyticsView,
        NotificationCategory.Billing => PlatformPermissions.BillingView,
        NotificationCategory.Knowledge => PlatformPermissions.KnowledgeManage,
        NotificationCategory.ChannelHealth => PlatformPermissions.WhatsAppManage,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown category."),
    };

    /// <summary>Whether this kind is addressed to one member rather than to every holder of a permission.</summary>
    public static bool IsPersonal(NotificationKind kind) =>
        NotificationKinds.CategoryOf(kind) == NotificationCategory.Reports;

    /// <param name="reportType">For report kinds: which report.</param>
    /// <param name="sourceId">The source row, for kinds whose action opens it.</param>
    /// <param name="connectionId">For WhatsApp: the connection the delivery used.</param>
    public static NotificationText Describe(
        NotificationKind kind,
        ReportType? reportType = null,
        Guid? sourceId = null,
        Guid? connectionId = null)
    {
        // The catalogue's own title, quoted: a fixed label from code, never a
        // workspace-authored schedule name.
        string report = reportType is ReportType type
            ? $"“{ReportCatalog.For(type).Title}”"
            : "requested";

        return kind switch
        {
            NotificationKind.ReportCompleted => new(
                "Your report is ready",
                $"Your {report} report has finished generating and is ready to download.",
                "/reports"),

            NotificationKind.ReportFailed => new(
                "Your report could not be generated",
                $"Your {report} report failed. Open Reports to see why.",
                "/reports"),

            NotificationKind.ScheduledReportCompleted => new(
                "Scheduled report ready",
                $"A scheduled {report} report has finished generating and is ready to download.",
                "/reports"),

            NotificationKind.ScheduledReportFailed => new(
                "Scheduled report generation failed",
                $"A scheduled {report} report could not be generated. Open Reports to see why.",
                "/reports"),

            NotificationKind.UsageWarning => Usage(70),

            NotificationKind.UsageCritical => Usage(90),

            NotificationKind.UsageExhausted => new(
                "Monthly AI operation limit reached",
                "This workspace has used all of its monthly AI operations. New AI operations are refused "
                + "until the limit resets at the start of next month (UTC).",
                "/plan-usage"),

            NotificationKind.BillingPastDue => new(
                "Subscription payment is past due",
                "A subscription payment did not succeed. The plan stays active during the grace period; "
                + "update the payment details in Billing to keep it.",
                "/billing"),

            NotificationKind.BillingSuspended => new(
                "Subscription suspended",
                "The grace period ended without a successful payment, so the workspace moved to the fallback "
                + "plan. Nothing was deleted.",
                "/billing"),

            NotificationKind.BillingRecovered => new(
                "Subscription active again",
                "The subscription is active again and the workspace has its plan back.",
                "/billing"),

            NotificationKind.BillingCanceled => new(
                "Subscription ended",
                "The subscription has ended and the workspace moved to the fallback plan. Nothing was deleted.",
                "/billing"),

            NotificationKind.KnowledgeIndexingFailed => new(
                "Knowledge indexing failed",
                "Indexing failed for a knowledge document, so it was not updated. Try indexing it again.",
                sourceId is Guid document
                    ? string.Create(CultureInfo.InvariantCulture, $"/knowledge/{document:D}")
                    : "/knowledge"),

            NotificationKind.WhatsAppDeliveryUnknown => new(
                "A WhatsApp delivery outcome is unknown",
                "A reply was handed to WhatsApp but its outcome could not be confirmed. It was not sent again, "
                + "so the customer does not receive it twice.",
                connectionId is Guid connection
                    ? string.Create(CultureInfo.InvariantCulture, $"/channels/whatsapp/{connection:D}")
                    : "/channels/whatsapp"),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind."),
        };
    }

    /// <summary>The fixed subject line an email for this kind carries.</summary>
    /// <remarks>
    /// Never interpolated. A header built from workspace-authored text is how a
    /// line break becomes a <c>Bcc:</c>; a subject that is a constant cannot.
    /// </remarks>
    public static string Subject(NotificationKind kind) => Subject(kind, UserLanguage.English);

    /// <summary>The fixed subject line in one language: <c>Nabra: …</c> or <c>نبرة: …</c>.</summary>
    public static string Subject(NotificationKind kind, UserLanguage language) =>
        EmailWords.Product(language) + ": " + Describe(kind, language).Title;

    /// <summary>
    /// What an email for this kind says, in the recipient's language.
    /// </summary>
    /// <remarks>
    /// The action path is the same as the in-app one. When the report type is not
    /// known any more (its source has been cleaned up), the sentence leaves the name
    /// out rather than guessing it.
    /// </remarks>
    public static NotificationText Describe(
        NotificationKind kind,
        UserLanguage language,
        ReportType? reportType = null,
        Guid? sourceId = null,
        Guid? connectionId = null)
    {
        string? actionPath = Describe(kind, reportType, sourceId, connectionId).ActionPath;
        string? report = reportType is ReportType type ? EmailWords.Isolate(ReportName(type, language)) : null;

        (string title, string body) = language == UserLanguage.Arabic
            ? Arabic(kind, report)
            : English(kind, report);

        return new NotificationText(title, body, actionPath);
    }

    /// <summary>A report's name in one language: the catalogue's own title, or its Arabic name.</summary>
    public static string ReportName(ReportType type, UserLanguage language) =>
        language != UserLanguage.Arabic
            ? ReportCatalog.For(type).Title
            : type switch
            {
                ReportType.ConversationActivity => "نشاط المحادثات",
                ReportType.ChannelUsage => "استخدام القنوات",
                ReportType.AiHumanHandling => "تولّي الذكاء الاصطناعي والفريق للمحادثات",
                ReportType.CustomerActivity => "نشاط العملاء",
                ReportType.AgentActivity => "نشاط وكلاء الذكاء الاصطناعي",
                ReportType.ProviderUsage => "استخدام مزوّدي الذكاء الاصطناعي",
                ReportType.ProviderCost => "التكلفة التقديرية للمزوّدين",
                ReportType.WhatsAppDelivery => "حالة تسليم رسائل واتساب",
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown report type."),
            };

    private static (string Title, string Body) English(NotificationKind kind, string? report) => kind switch
    {
        NotificationKind.ReportCompleted => (
            "Your report is ready",
            report is null
                ? "Your report has finished generating and is ready to download."
                : $"Your “{report}” report has finished generating and is ready to download."),

        NotificationKind.ReportFailed => (
            "Your report could not be generated",
            report is null
                ? "Your report failed. Open Reports to see why."
                : $"Your “{report}” report failed. Open Reports to see why."),

        NotificationKind.ScheduledReportCompleted => (
            "Scheduled report ready",
            report is null
                ? "A scheduled report has finished generating and is ready to download."
                : $"A scheduled “{report}” report has finished generating and is ready to download."),

        NotificationKind.ScheduledReportFailed => (
            "Scheduled report generation failed",
            report is null
                ? "A scheduled report could not be generated. Open Reports to see why."
                : $"A scheduled “{report}” report could not be generated. Open Reports to see why."),

        _ => (Describe(kind).Title, Describe(kind).Body),
    };

    /// <summary>
    /// The Arabic sentences, word for word the client's own (messages.ar.json,
    /// <c>notification.*</c>), which follow GLOSSARY.md.
    /// </summary>
    private static (string Title, string Body) Arabic(NotificationKind kind, string? report) => kind switch
    {
        NotificationKind.ReportCompleted => (
            "تقريرك جاهز",
            report is null
                ? "اكتمل إنشاء تقريرك وهو جاهز للتنزيل."
                : $"اكتمل إنشاء تقريرك «{report}» وهو جاهز للتنزيل."),

        NotificationKind.ReportFailed => (
            "تعذّر إنشاء تقريرك",
            report is null
                ? "فشل إنشاء تقريرك. افتح التقارير لمعرفة السبب."
                : $"فشل إنشاء تقريرك «{report}». افتح التقارير لمعرفة السبب."),

        NotificationKind.ScheduledReportCompleted => (
            "التقرير المجدول جاهز",
            report is null
                ? "اكتمل إنشاء تقرير مجدول وهو جاهز للتنزيل."
                : $"اكتمل إنشاء تقرير «{report}» مجدول وهو جاهز للتنزيل."),

        NotificationKind.ScheduledReportFailed => (
            "فشل إنشاء التقرير المجدول",
            report is null
                ? "تعذّر إنشاء تقرير مجدول. افتح التقارير لمعرفة السبب."
                : $"تعذّر إنشاء تقرير «{report}» مجدول. افتح التقارير لمعرفة السبب."),

        NotificationKind.UsageWarning or NotificationKind.UsageCritical => (
            string.Create(CultureInfo.InvariantCulture, $"بلغ استخدام الذكاء الاصطناعي {PercentOf(kind)}% من الحد الشهري"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"استخدمت مساحة العمل هذه {PercentOf(kind)}% من عمليات الذكاء الاصطناعي الشهرية. لا يُرفض شيء حتى بلوغ الحد.")),

        NotificationKind.UsageExhausted => (
            "بلغت عمليات الذكاء الاصطناعي حدّها الشهري",
            "استخدمت مساحة العمل هذه كل عمليات الذكاء الاصطناعي الشهرية. تُرفض العمليات الجديدة حتى يتجدّد الحد مطلع الشهر القادم (بالتوقيت العالمي)."),

        NotificationKind.BillingPastDue => (
            "دفعة الاشتراك متأخرة",
            "لم تنجح إحدى دفعات الاشتراك. تبقى الباقة سارية خلال مهلة السماح؛ حدّث بيانات الدفع في الفوترة للاحتفاظ بها."),

        NotificationKind.BillingSuspended => (
            "عُلّق الاشتراك",
            "انتهت مهلة السماح دون دفعة ناجحة، فانتقلت مساحة العمل إلى الباقة البديلة. لم يُحذف شيء."),

        NotificationKind.BillingRecovered => (
            "عاد الاشتراك ساريًا",
            "عاد الاشتراك ساريًا واستعادت مساحة العمل باقتها."),

        NotificationKind.BillingCanceled => (
            "انتهى الاشتراك",
            "انتهى الاشتراك وانتقلت مساحة العمل إلى الباقة البديلة. لم يُحذف شيء."),

        NotificationKind.KnowledgeIndexingFailed => (
            "فشلت فهرسة المعرفة",
            "فشلت فهرسة أحد مستندات المعرفة، فلم يُحدَّث. أعد محاولة فهرسته."),

        NotificationKind.WhatsAppDeliveryUnknown => (
            "نتيجة تسليم رسالة واتساب غير معروفة",
            "سُلّم ردّ إلى واتساب لكن تعذّر تأكيد نتيجته. لم يُرسَل مرة أخرى، كي لا يتلقاه العميل مرتين."),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown notification kind."),
    };

    private static int PercentOf(NotificationKind kind) => kind == NotificationKind.UsageCritical ? 90 : 70;

    private static NotificationText Usage(int percent) =>
        new(
            string.Create(CultureInfo.InvariantCulture, $"AI usage has reached {percent}% of the monthly limit"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"This workspace has used {percent}% of its monthly AI operations. Nothing is refused until the limit is reached."),
            "/plan-usage");
}
