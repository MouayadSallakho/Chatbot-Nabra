using System.Globalization;
using OmniAI.Application.Reports;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;

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
    public static string Subject(NotificationKind kind) => "Nabra: " + Describe(kind).Title;

    private static NotificationText Usage(int percent) =>
        new(
            string.Create(CultureInfo.InvariantCulture, $"AI usage has reached {percent}% of the monthly limit"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"This workspace has used {percent}% of its monthly AI operations. Nothing is refused until the limit is reached."),
            "/plan-usage");
}
