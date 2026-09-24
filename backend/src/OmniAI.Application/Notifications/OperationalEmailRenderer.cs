using System.Net;
using System.Text;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>
/// Turns a notification into an operational email.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fixed markup, encoded values.</strong> The HTML is a constant shape;
/// every value placed in it — the workspace name above all, which a workspace
/// wrote — is HTML-encoded, so <c>&lt;script&gt;</c> arrives as text. No value
/// is ever placed in an attribute except the link, which is built from trusted
/// configuration and a validated internal path.
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
/// </remarks>
public static class OperationalEmailRenderer
{
    public static OperationalEmailMessage Render(
        Notification notification,
        EmailAddress to,
        string? workspaceName,
        OperationalEmailLinks links)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(links);

        string workspace = SingleLine(workspaceName);
        string link = links.For(notification.ActionPath).AbsoluteUri;
        string action = ActionLabel(notification.Category);

        StringBuilder text = new();
        text.Append("OmniAI").Append('\n');

        if (workspace.Length > 0)
        {
            text.Append("Workspace: ").Append(workspace).Append('\n');
        }

        text.Append('\n')
            .Append(notification.Title).Append('\n')
            .Append('\n')
            .Append(notification.Body).Append('\n')
            .Append('\n')
            .Append(action).Append(": ").Append(link).Append('\n')
            .Append('\n')
            .Append(Footer).Append('\n');

        StringBuilder html = new();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width\"><title>")
            .Append(Encode(notification.Title))
            .Append("</title></head>")
            .Append("<body style=\"margin:0;padding:24px;background:#f4f5f7;font-family:Arial,Helvetica,sans-serif;color:#1f232b;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #dfe1e6;border-radius:8px;\">")
            .Append("<tr><td style=\"padding:24px;\">")
            .Append("<p style=\"margin:0 0 16px;font-weight:bold;font-size:16px;\">OmniAI</p>");

        if (workspace.Length > 0)
        {
            html.Append("<p style=\"margin:0 0 16px;font-size:13px;color:#5e6470;\">Workspace: ")
                .Append(Encode(workspace))
                .Append("</p>");
        }

        html.Append("<h1 style=\"margin:0 0 12px;font-size:20px;\">")
            .Append(Encode(notification.Title))
            .Append("</h1>")
            .Append("<p style=\"margin:0 0 20px;font-size:15px;line-height:1.5;\">")
            .Append(Encode(notification.Body))
            .Append("</p>")
            .Append("<p style=\"margin:0 0 24px;\"><a href=\"")
            .Append(Encode(link))
            .Append("\" style=\"display:inline-block;padding:10px 16px;background:#2f5bd3;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold;\">")
            .Append(Encode(action))
            .Append("</a></p>")
            .Append("<p style=\"margin:0;font-size:12px;color:#5e6470;line-height:1.5;\">")
            .Append(Encode(Footer))
            .Append("</p></td></tr></table></body></html>");

        return new OperationalEmailMessage(
            to,
            NotificationCatalog.Subject(notification.Kind),
            text.ToString(),
            html.ToString());
    }

    private const string Footer =
        "You are receiving this operational email because you turned it on in your OmniAI notification "
        + "preferences. You can turn it off there at any time.";

    private static string ActionLabel(NotificationCategory category) => category switch
    {
        NotificationCategory.Reports => "Open Reports",
        NotificationCategory.Usage => "Open Plan & usage",
        NotificationCategory.Billing => "Open Billing",
        NotificationCategory.Knowledge => "Open Knowledge",
        NotificationCategory.ChannelHealth => "Open WhatsApp",
        _ => "Open OmniAI",
    };

    /// <summary>
    /// A label reduced to one line of visible text.
    /// </summary>
    /// <remarks>
    /// Line breaks and other control characters become spaces. The body is not a
    /// header, so this is not the injection defence — the constant subject is —
    /// but a workspace name has no business spanning lines in either part.
    /// </remarks>
    private static string SingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder line = new(value.Length);

        foreach (char character in value)
        {
            line.Append(char.IsControl(character) ? ' ' : character);
        }

        return line.ToString().Trim();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
