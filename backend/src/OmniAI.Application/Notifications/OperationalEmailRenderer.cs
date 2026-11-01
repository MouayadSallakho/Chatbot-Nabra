using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>
/// Turns a notification into an operational email.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fixed markup, encoded values.</strong> The HTML is a constant shape
/// (<see cref="EmailLayout"/>); every value placed in it — the workspace name above
/// all, which a workspace wrote — is HTML-encoded, so <c>&lt;script&gt;</c> arrives
/// as text. No value is ever placed in an attribute except the link, which is
/// built from trusted configuration and a validated internal path.
/// </para>
/// <para>
/// <strong>Nothing to load.</strong> No image, no web font, no tracking pixel and
/// no remote stylesheet: opening the email makes no request, so it tells nobody
/// that it was opened.
/// </para>
/// <para>
/// <strong>No attachment and no secret.</strong> A report email says the report is
/// ready and links to Reports, where the authenticated download is the only way
/// to the file. The link carries no token, session or key.
/// </para>
/// <para>
/// <strong>The subject is a constant.</strong> Workspace-authored text never
/// reaches a header, so a line break in a workspace name cannot add a
/// <c>Bcc:</c>.
/// </para>
/// <para>
/// <strong>In the recipient's language (Phase 37.3).</strong> The dispatcher passes
/// the recipient's stored language and, for report kinds, the report type, and the
/// words come from <see cref="NotificationCatalog"/> in that language. Without a
/// language the stored English text is used, as before.
/// </para>
/// </remarks>
public static class OperationalEmailRenderer
{
    /// <summary>The stored English text, as before Phase 37.3.</summary>
    public static OperationalEmailMessage Render(
        Notification notification,
        EmailAddress to,
        string? workspaceName,
        OperationalEmailLinks links)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return Compose(
            notification,
            to,
            workspaceName,
            links,
            UserLanguage.English,
            new NotificationText(notification.Title, notification.Body, notification.ActionPath));
    }

    /// <summary>The same email, worded in the recipient's language.</summary>
    /// <param name="reportType">For report kinds, when the report is still known.</param>
    public static OperationalEmailMessage Render(
        Notification notification,
        EmailAddress to,
        string? workspaceName,
        OperationalEmailLinks links,
        UserLanguage language,
        ReportType? reportType)
    {
        ArgumentNullException.ThrowIfNull(notification);

        NotificationText words = NotificationCatalog.Describe(notification.Kind, language, reportType);

        // The link is the one the notification was created with: the words change
        // with the language, where they lead does not.
        return Compose(
            notification,
            to,
            workspaceName,
            links,
            language,
            words with { ActionPath = notification.ActionPath });
    }

    private static OperationalEmailMessage Compose(
        Notification notification,
        EmailAddress to,
        string? workspaceName,
        OperationalEmailLinks links,
        UserLanguage language,
        NotificationText words)
    {
        ArgumentNullException.ThrowIfNull(links);

        (string text, string html) = EmailLayout.Compose(
            language,
            EmailLayout.SingleLine(workspaceName),
            words.Title,
            [words.Body],
            (ActionLabel(notification.Category, language), links.For(words.ActionPath)),
            Footer(language));

        return new OperationalEmailMessage(
            to,
            NotificationCatalog.Subject(notification.Kind, language),
            text,
            html);
    }

    private static string Footer(UserLanguage language) => language == UserLanguage.Arabic
        ? "تصلك هذه الرسالة التشغيلية لأنك فعّلتها في تفضيلات الإشعارات في نبرة. يمكنك إيقافها من هناك في أي وقت."
        : "You are receiving this operational email because you turned it on in your Nabra notification "
            + "preferences. You can turn it off there at any time.";

    /// <summary>The button's words: the client's own <c>notifications.action.*</c>.</summary>
    private static string ActionLabel(NotificationCategory category, UserLanguage language) => language == UserLanguage.Arabic
        ? category switch
        {
            NotificationCategory.Reports => "فتح التقارير",
            NotificationCategory.Usage => "فتح الباقة والاستخدام",
            NotificationCategory.Billing => "فتح الفوترة",
            NotificationCategory.Knowledge => "فتح المعرفة",
            NotificationCategory.ChannelHealth => "فتح واتساب",
            _ => "فتح نبرة",
        }
        : category switch
        {
            NotificationCategory.Reports => "Open Reports",
            NotificationCategory.Usage => "Open Plan & usage",
            NotificationCategory.Billing => "Open Billing",
            NotificationCategory.Knowledge => "Open Knowledge",
            NotificationCategory.ChannelHealth => "Open WhatsApp",
            _ => "Open Nabra",
        };
}
