using System.Buffers.Binary;
using OmniAI.Application.Accounts;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Notifications;
using OmniAI.Domain.Reports;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Infrastructure.Notifications.Email;
using MimeKit;

namespace OmniAI.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// Every email Nabra sends, in both languages, built into the very MIME message
/// the SMTP adapter hands over (Phase 39.8a): the header mark travels inside it
/// by Content-ID, the plain-text part stays, and nothing is fetched.
/// </summary>
/// <remarks>
/// With <c>OMNIAI_EMAIL_GALLERY_DIR</c> set, each message is also written there
/// as an <c>.eml</c> file, so the gallery can be delivered to a local Mailpit and
/// looked at in a mail client. The links are placeholders: no token, no secret.
/// </remarks>
public sealed class EmailTemplateGalleryTests
{
    private static readonly OperationalEmailLinks Links = new(new Uri("http://localhost:4200/"));

    private static readonly EmailAddress To = EmailAddress.Create("member@example.test");

    public static TheoryData<string, UserLanguage> Templates()
    {
        TheoryData<string, UserLanguage> data = [];

        foreach (UserLanguage language in new[] { UserLanguage.English, UserLanguage.Arabic })
        {
            foreach (string name in new[] { "PasswordReset", "PasswordChanged", "Invitation" })
            {
                data.Add(name, language);
            }

            foreach (NotificationKind kind in Enum.GetValues<NotificationKind>())
            {
                data.Add(kind.ToString(), language);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_email_carries_the_mark_inline_and_keeps_its_text_part(string template, UserLanguage language)
    {
        OperationalEmailMessage email = Render(template, language);
        MimeMessage mime = new SmtpOperationalEmailSender(Options()).Build(email);
        bool arabic = language == UserLanguage.Arabic;

        // multipart/alternative: the plain text, then the HTML with its mark.
        Multipart alternative = Assert.IsType<MultipartAlternative>(mime.Body);
        TextPart text = Assert.IsType<TextPart>(alternative[0]);
        MultipartRelated related = Assert.IsType<MultipartRelated>(alternative[1]);
        TextPart html = Assert.IsType<TextPart>(related[0]);
        MimePart mark = Assert.IsType<MimePart>(Assert.Single(related, part => part != html));

        Assert.Equal("text/plain", text.ContentType.MimeType);
        Assert.StartsWith(arabic ? "نبرة\n" : "Nabra\n", text.Text, StringComparison.Ordinal);
        Assert.Equal("text/html", html.ContentType.MimeType);

        // The mark: a PNG by Content-ID, inline (never listed as an attachment).
        Assert.Equal("image/png", mark.ContentType.MimeType);
        Assert.Equal(EmailLayout.MarkContentId, mark.ContentId);
        Assert.Equal(ContentDisposition.Inline, mark.ContentDisposition?.Disposition);
        Assert.False(mark.IsAttachment);
        (int width, int height) = PngSize(EmailBrand.Mark);
        Assert.Equal((56, 48), (width, height));

        // The HTML shows it once, by that id, with the name beside it; nothing remote.
        Assert.Single(html.Text.Split("<img", StringSplitOptions.None)[1..]);
        Assert.Contains("<img src=\"cid:" + EmailLayout.MarkContentId + "\" width=\"28\" height=\"24\" alt=\"\"", html.Text, StringComparison.Ordinal);
        Assert.Contains(">" + EmailWords.Product(language) + "</td>", html.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", html.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=\"http", html.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", html.Text, StringComparison.OrdinalIgnoreCase);

        // Laid out for its language, with fonts that fall back well.
        Assert.Contains(arabic ? "<html lang=\"ar\" dir=\"rtl\">" : "<html lang=\"en\" dir=\"ltr\">", html.Text, StringComparison.Ordinal);
        Assert.Contains(arabic ? "'IBM Plex Sans Arabic','Segoe UI',Tahoma" : "'Geist',-apple-system", html.Text, StringComparison.Ordinal);

        // Calm Glass: the emerald pill for the action, never the old blue.
        Assert.Contains("background:#235347", html.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#2f5bd3", html.Text, StringComparison.OrdinalIgnoreCase);

        // No email shows a conversation, so no voice bubble colour appears.
        Assert.DoesNotContain("#daf1de", html.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#f5ecd8", html.Text, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("omniai", email.Subject + text.Text + html.Text, StringComparison.OrdinalIgnoreCase);

        WriteToGallery(template, language, mime);
    }

    [Fact]
    public void A_message_that_shows_no_mark_carries_none()
    {
        OperationalEmailMessage plain = new(To, "Nabra: test", "Nabra\n\nHello\n", "<!DOCTYPE html><html><body><p>Hello</p></body></html>");

        MimeMessage mime = new SmtpOperationalEmailSender(Options()).Build(plain);

        MultipartAlternative alternative = Assert.IsType<MultipartAlternative>(mime.Body);
        Assert.All(alternative, part => Assert.IsType<TextPart>(part));
        Assert.DoesNotContain(mime.BodyParts, part => part.ContentType.MediaType == "image");
    }

    private static OperationalEmailMessage Render(string template, UserLanguage language) => template switch
    {
        "PasswordReset" => AccountEmailRenderer.PasswordReset(
            To, language, Links.WithFragment("/reset-password", "token=placeholder"), TimeSpan.FromMinutes(30)),
        "PasswordChanged" => AccountEmailRenderer.PasswordChanged(To, language, Links.For("/sign-in")),
        "Invitation" => AccountEmailRenderer.Invitation(
            To,
            language,
            language == UserLanguage.Arabic ? "شركة النور" : "Northwind Trading",
            new DateTimeOffset(2026, 11, 23, 9, 5, 0, TimeSpan.Zero),
            Links.WithFragment("/accept-invitation", "token=placeholder")),
        _ => OperationalEmailRenderer.Render(
            NotificationOf(Enum.Parse<NotificationKind>(template)),
            To,
            language == UserLanguage.Arabic ? "شركة النور" : "Northwind Trading",
            Links,
            language,
            ReportType.ConversationActivity),
    };

    private static Notification NotificationOf(NotificationKind kind)
    {
        NotificationText text = NotificationCatalog.Describe(kind, ReportType.ConversationActivity);

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

    private static OperationalEmailOptions Options()
    {
        OperationalEmailOptions options = new()
        {
            AppBaseUrl = "http://localhost:4200",
            Smtp = new SmtpSettings
            {
                Host = "127.0.0.1",
                Port = 1025,
                Security = SmtpSecurity.None,
                FromAddress = "nabra@nabra.local",
                Timeout = TimeSpan.FromSeconds(5),
            },
        };

        options.Validate();

        return options;
    }

    /// <summary>A PNG's width and height, from its IHDR chunk.</summary>
    private static (int Width, int Height) PngSize(byte[] png)
    {
        Assert.True(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));

        return (
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
    }

    private static void WriteToGallery(string template, UserLanguage language, MimeMessage mime)
    {
        string? folder = Environment.GetEnvironmentVariable("OMNIAI_EMAIL_GALLERY_DIR");

        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        string code = language == UserLanguage.Arabic ? "ar" : "en";

        mime.WriteTo(Path.Combine(folder, $"{template}-{code}.eml"));
    }
}
