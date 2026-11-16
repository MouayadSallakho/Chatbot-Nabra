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
/// <strong>Nothing to load.</strong> No remote image, web font, remote stylesheet
/// or tracking pixel, so opening an email tells nobody that it was opened. The
/// one picture, the brand's mark in the header, travels inside the message as
/// an inline part referenced by Content-ID (<see cref="MarkContentId"/>): never
/// a link, never inline SVG and never a <c>data:</c> URI, which Gmail, Outlook
/// and Apple Mail do not all show. The sender attaches it; a client that hides
/// images still shows the name beside it.
/// </para>
/// <para>
/// <strong>Calm Glass, in what an email client can draw (Phase 39.8a).</strong>
/// The cream ground, a white card with a sand line, ink text, quiet text in
/// stone, and one emerald pill for the action. Font stacks that read well when
/// no web font loads: the system's sans in English, and Segoe UI, Tahoma, Geeza
/// Pro or Noto Sans Arabic in Arabic, with Arabic's taller lines. No email
/// shows a conversation today, so the three voices' colours appear nowhere.
/// </para>
/// </remarks>
public static class EmailLayout
{
    /// <summary>
    /// The Content-ID of the brand's mark, which the HTML part shows as
    /// <c>cid:nabra-mark@nabra</c> and the sender attaches as an inline part.
    /// </summary>
    public const string MarkContentId = "nabra-mark@nabra";

    // Calm Glass colours (calm-glass.md section 2), as an email can carry them.
    private const string Ground = "#f4f1ea"; // cream-100
    private const string Panel = "#ffffff";
    private const string Line = "#e3dccc"; // sand-300
    private const string Ink = "#1b2420"; // ink-900: 15.2:1 on cream-50, more on white
    private const string Secondary = "#3a4540"; // ink-700
    private const string Quiet = "#6b6658"; // stone-600: 5.7:1 on white
    private const string Deep = "#0b2b26"; // emerald-900: the name beside the mark
    private const string Primary = "#235347"; // emerald-700: white on it 8.8:1

    private const string LatinFonts =
        "'Geist',-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Helvetica,Arial,sans-serif";

    private const string ArabicFonts =
        "'IBM Plex Sans Arabic','Segoe UI',Tahoma,'Geeza Pro','Noto Sans Arabic','Arabic Typesetting',Arial,sans-serif";

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

        string fonts = arabic ? ArabicFonts : LatinFonts;
        string body = arabic ? "15px;line-height:27px" : "15px;line-height:24px";
        string small = arabic ? "13px;line-height:22px" : "12px;line-height:18px";

        StringBuilder html = new();
        html.Append("<!DOCTYPE html><html lang=\"").Append(lang).Append("\" dir=\"").Append(dir).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width\">")
            .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\"><title>")
            .Append(Encode(heading))
            .Append("</title></head>")
            .Append("<body dir=\"").Append(dir).Append("\" style=\"margin:0;padding:24px 12px;background:").Append(Ground)
            .Append(";font-family:").Append(fonts).Append(";color:").Append(Ink).Append(";\">")
            .Append("<table role=\"presentation\" dir=\"").Append(dir).Append("\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;margin:0 auto;background:")
            .Append(Panel).Append(";border:1px solid ").Append(Line).Append(";border-radius:24px;\">")
            .Append("<tr><td style=\"padding:28px 28px 24px;\">");

        // The header: the mark, then the name in the reader's language, on one
        // line; in Arabic the row runs right to left, so the mark leads there too.
        html.Append("<table role=\"presentation\" dir=\"").Append(dir).Append("\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:0 0 20px;\"><tr>")
            .Append("<td style=\"padding:0;vertical-align:middle;\"><img src=\"cid:").Append(MarkContentId)
            .Append("\" width=\"28\" height=\"24\" alt=\"\" style=\"display:block;border:0;width:28px;height:24px;\"></td>")
            .Append("<td style=\"padding:0 10px;vertical-align:middle;font-family:").Append(fonts)
            .Append(";font-size:17px;line-height:24px;font-weight:600;color:").Append(Deep).Append(";\">")
            .Append(Encode(product))
            .Append("</td></tr></table>");

        if (workspaceLine.Length > 0)
        {
            html.Append("<p style=\"margin:0 0 16px;font-size:").Append(small).Append(";color:").Append(Quiet).Append(";\">")
                .Append(Encode(workspaceLine))
                .Append("</p>");
        }

        html.Append("<h1 style=\"margin:0 0 12px;font-size:22px;line-height:").Append(arabic ? "34px" : "30px")
            .Append(";font-weight:600;color:").Append(Ink).Append(";\">")
            .Append(Encode(heading))
            .Append("</h1>");

        foreach (string paragraph in paragraphs)
        {
            html.Append("<p style=\"margin:0 0 18px;font-size:").Append(body).Append(";color:").Append(Secondary).Append(";\">")
                .Append(Encode(paragraph))
                .Append("</p>");
        }

        if (action is (string actionLabel, Uri actionLink))
        {
            html.Append("<p style=\"margin:4px 0 24px;\"><a href=\"")
                .Append(WebUtility.HtmlEncode(actionLink.AbsoluteUri))
                .Append("\" style=\"display:inline-block;padding:12px 22px;background:").Append(Primary)
                .Append(";color:#ffffff;text-decoration:none;border-radius:999px;font-weight:600;font-size:15px;line-height:20px;\">")
                .Append(Encode(actionLabel))
                .Append("</a></p>");
        }

        html.Append("<p style=\"margin:0;padding-top:16px;border-top:1px solid ").Append(Line).Append(";font-size:").Append(small)
            .Append(";color:").Append(Quiet).Append(";\">")
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
