using System.Net;
using System.Text;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>The few words every email shares, in both languages.</summary>
public static class EmailWords
{
    /// <summary>First Strong Isolate: starts a run whose direction is its own.</summary>
    public const char FirstStrongIsolate = '\u2068';

    /// <summary>Pop Directional Isolate: ends it.</summary>
    public const char PopDirectionalIsolate = '\u2069';

    /// <summary>The product's public name: Nabra, or نبرة.</summary>
    public static string Product(UserLanguage language) => language == UserLanguage.Arabic ? "نبرة" : "Nabra";

    /// <summary>
    /// Wraps a name somebody wrote (a workspace, a report) so it keeps its own
    /// direction: an English name inside an Arabic sentence, or the reverse.
    /// </summary>
    /// <remarks>
    /// FSI…PDI in plain text; <see cref="EmailLayout"/> turns them into
    /// <c>&lt;bdi&gt;</c> in HTML.
    /// </remarks>
    public static string Isolate(string value) =>
        string.Concat(FirstStrongIsolate.ToString(), value, PopDirectionalIsolate.ToString());

    public static string Workspace(UserLanguage language) => language == UserLanguage.Arabic ? "مساحة العمل" : "Workspace";
}

/// <summary>
/// The one shape every Nabra email has: a plain-text part and an HTML part
/// saying the same words.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fixed markup, encoded values.</strong> Every value placed in the HTML
/// is encoded, so <c>&lt;script&gt;</c> arrives as text. No value is placed in an
/// attribute except the one link, which the caller builds from trusted
/// configuration.
/// </para>
/// <para>
/// <strong>Both directions.</strong> Arabic is <c>lang="ar" dir="rtl"</c>, English
/// <c>lang="en" dir="ltr"</c>, on the document and on the card, so a client that
/// drops one still lays the text out the right way. Names somebody wrote are
/// isolated: FSI…PDI in text, <c>&lt;bdi&gt;</c> in HTML.
/// </para>
/// <para>
/// <strong>Nothing to load.</strong> No image, web font, remote stylesheet or
/// tracking pixel, so opening an email tells nobody that it was opened.
/// </para>
/// </remarks>
public static class EmailLayout
{
    /// <param name="language">The reader's language.</param>
    /// <param name="workspace">A workspace name, already reduced to one line, or empty.</param>
    /// <param name="heading">The email's title.</param>
    /// <param name="paragraphs">The body, one entry per paragraph.</param>
    /// <param name="action">The one button and its absolute link, or none.</param>
    /// <param name="footer">Why this email arrived.</param>
    public static (string Text, string Html) Compose(
        UserLanguage language,
        string workspace,
        string heading,
        IReadOnlyList<string> paragraphs,
        (string Label, Uri Link)? action,
        string footer)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        bool arabic = language == UserLanguage.Arabic;
        string lang = arabic ? "ar" : "en";
        string dir = arabic ? "rtl" : "ltr";
        string product = EmailWords.Product(language);
        string workspaceLine = workspace.Length > 0
            ? EmailWords.Workspace(language) + ": " + EmailWords.Isolate(workspace)
            : string.Empty;

        StringBuilder text = new();
        text.Append(product).Append('\n');

        if (workspaceLine.Length > 0)
        {
            text.Append(workspaceLine).Append('\n');
        }

        text.Append('\n').Append(heading).Append('\n');

        foreach (string paragraph in paragraphs)
        {
            text.Append('\n').Append(paragraph).Append('\n');
        }

        if (action is (string label, Uri link))
        {
            text.Append('\n').Append(label).Append(": ").Append(link.AbsoluteUri).Append('\n');
        }

        text.Append('\n').Append(footer).Append('\n');

        StringBuilder html = new();
        html.Append("<!DOCTYPE html><html lang=\"").Append(lang).Append("\" dir=\"").Append(dir).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width\"><title>")
            .Append(Encode(heading))
            .Append("</title></head>")
            .Append("<body dir=\"").Append(dir).Append("\" style=\"margin:0;padding:24px;background:#f4f5f7;font-family:Arial,Helvetica,sans-serif;color:#1f232b;\">")
            .Append("<table role=\"presentation\" dir=\"").Append(dir).Append("\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #dfe1e6;border-radius:8px;\">")
            .Append("<tr><td style=\"padding:24px;\">")
            .Append("<p style=\"margin:0 0 16px;font-weight:bold;font-size:16px;\">").Append(Encode(product)).Append("</p>");

        if (workspaceLine.Length > 0)
        {
            html.Append("<p style=\"margin:0 0 16px;font-size:13px;color:#5e6470;\">")
                .Append(Encode(workspaceLine))
                .Append("</p>");
        }

        html.Append("<h1 style=\"margin:0 0 12px;font-size:20px;\">")
            .Append(Encode(heading))
            .Append("</h1>");

        foreach (string paragraph in paragraphs)
        {
            html.Append("<p style=\"margin:0 0 20px;font-size:15px;line-height:1.5;\">")
                .Append(Encode(paragraph))
                .Append("</p>");
        }

        if (action is (string actionLabel, Uri actionLink))
        {
            html.Append("<p style=\"margin:0 0 24px;\"><a href=\"")
                .Append(WebUtility.HtmlEncode(actionLink.AbsoluteUri))
                .Append("\" style=\"display:inline-block;padding:10px 16px;background:#2f5bd3;color:#ffffff;text-decoration:none;border-radius:6px;font-weight:bold;\">")
                .Append(Encode(actionLabel))
                .Append("</a></p>");
        }

        html.Append("<p style=\"margin:0;font-size:12px;color:#5e6470;line-height:1.5;\">")
            .Append(Encode(footer))
            .Append("</p></td></tr></table></body></html>");

        return (text.ToString(), html.ToString());
    }

    /// <summary>
    /// A label reduced to one line of visible text.
    /// </summary>
    /// <remarks>
    /// Line breaks and other control characters become spaces, and so do stray
    /// directional isolates, so only the ones this layout adds survive. The body is
    /// not a header, so this is not the injection defence (the constant subject is),
    /// but a workspace name has no business spanning lines in either part.
    /// </remarks>
    public static string SingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder line = new(value.Length);

        foreach (char character in value)
        {
            line.Append(char.IsControl(character) || IsIsolateControl(character) ? ' ' : character);
        }

        return line.ToString().Trim();
    }

    /// <summary>HTML-encodes, then turns the layout's own isolates into <c>&lt;bdi&gt;</c>.</summary>
    private static string Encode(string value) =>
        WebUtility.HtmlEncode(value)
            .Replace(EmailWords.FirstStrongIsolate.ToString(), "<bdi>", StringComparison.Ordinal)
            .Replace(EmailWords.PopDirectionalIsolate.ToString(), "</bdi>", StringComparison.Ordinal);

    private static bool IsIsolateControl(char character) => character is >= '\u2066' and <= '\u2069';
}
