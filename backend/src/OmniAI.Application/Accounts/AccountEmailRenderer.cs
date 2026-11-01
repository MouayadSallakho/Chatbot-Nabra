using System.Globalization;
using OmniAI.Application.Notifications;
using OmniAI.Domain.Accounts;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Accounts;

/// <summary>
/// The three account emails: set a new password, your password was changed, and
/// an invitation. English and Arabic.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Same conventions as the operational emails.</strong> One
/// <see cref="EmailLayout"/>, a constant subject per kind and language, every value
/// encoded, nothing to load, and names somebody wrote (a workspace) isolated.
/// </para>
/// <para>
/// <strong>Unlike the operational emails, two of them carry a secret.</strong> A
/// reset link and an invitation link hold a token in the URL fragment, so it never
/// reaches a server log or a <c>Referer</c>. The message exists only in memory
/// while it is sent; nothing here logs or stores it.
/// </para>
/// <para>
/// <strong>Instants are written the same way in both languages:</strong>
/// <c>yyyy-MM-dd HH:mm</c>, UTC, Western digits (localisation decision 3), and
/// isolated so the digits read left to right inside Arabic.
/// </para>
/// </remarks>
public static class AccountEmailRenderer
{
    /// <summary>The fixed subject for a kind in a language.</summary>
    public static string Subject(AccountEmailKind kind, UserLanguage language) =>
        EmailWords.Product(language) + ": " + kind switch
        {
            AccountEmailKind.PasswordReset => language == UserLanguage.Arabic ? "تعيين كلمة مرور جديدة" : "Set a new password",
            AccountEmailKind.PasswordChanged => language == UserLanguage.Arabic ? "تم تغيير كلمة المرور" : "Your password was changed",
            AccountEmailKind.Invitation => language == UserLanguage.Arabic ? "تلقّيت دعوة إلى مساحة عمل" : "You have been invited to a workspace",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account email kind."),
        };

    /// <param name="link">The reset page, with the user and the token in its fragment.</param>
    /// <param name="lifetime">How long the link works, as configured.</param>
    public static OperationalEmailMessage PasswordReset(
        EmailAddress to,
        UserLanguage language,
        Uri link,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(link);

        bool arabic = language == UserLanguage.Arabic;
        int minutes = Math.Max(1, (int)Math.Round(lifetime.TotalMinutes));

        (string text, string html) = EmailLayout.Compose(
            language,
            string.Empty,
            arabic ? "تعيين كلمة مرور جديدة" : "Set a new password",
            arabic
                ? [
                    "طلب أحدهم تعيين كلمة مرور جديدة لحساب نبرة الذي يستخدم هذا العنوان. إن كنت أنت، فاستخدم الزر أدناه. يعمل الرابط مدة "
                        + ArabicMinutes(minutes) + " ولمرة واحدة فقط.",
                    "إن لم تطلب ذلك، فتجاهل هذه الرسالة؛ ستبقى كلمة المرور كما هي.",
                ]
                : [
                    "Somebody asked to set a new password for the Nabra account that uses this address. If it was you, use the button below. "
                        + "The link works for " + EnglishMinutes(minutes) + ", and only once.",
                    "If you did not ask for this, you can ignore this email: your password stays as it is.",
                ],
            (arabic ? "تعيين كلمة مرور جديدة" : "Set a new password", link),
            arabic
                ? "تصلك هذه الرسالة لأن أحدهم طلب إعادة تعيين كلمة المرور لحسابك في نبرة."
                : "You are receiving this email because a password reset was requested for your Nabra account.");

        return new OperationalEmailMessage(to, Subject(AccountEmailKind.PasswordReset, language), text, html);
    }

    /// <param name="signIn">The sign-in page. No token, nothing to act on but a normal sign-in.</param>
    public static OperationalEmailMessage PasswordChanged(
        EmailAddress to,
        UserLanguage language,
        Uri signIn)
    {
        ArgumentNullException.ThrowIfNull(signIn);

        bool arabic = language == UserLanguage.Arabic;

        (string text, string html) = EmailLayout.Compose(
            language,
            string.Empty,
            arabic ? "تم تغيير كلمة المرور" : "Your password was changed",
            arabic
                ? [
                    "تم تغيير كلمة المرور لحساب نبرة الذي يستخدم هذا العنوان، وسُجّل الخروج من كل الجلسات التي كانت مفتوحة.",
                    "إن لم تقم بذلك، فاطلب كلمة مرور جديدة من صفحة تسجيل الدخول فورًا، وأبلغ المسؤول عن مساحة العمل.",
                ]
                : [
                    "The password for the Nabra account that uses this address was changed, and every session that was signed in has been signed out.",
                    "If you did not do this, ask for a new password from the sign-in page at once, and tell your workspace's administrator.",
                ],
            (arabic ? "تسجيل الدخول" : "Sign in", signIn),
            arabic
                ? "تصلك هذه الرسالة لأن كلمة مرور حسابك في نبرة تغيّرت. تُرسل مهما كانت تفضيلات الإشعارات."
                : "You are receiving this email because the password of your Nabra account was changed. It is sent whatever your notification preferences are.");

        return new OperationalEmailMessage(to, Subject(AccountEmailKind.PasswordChanged, language), text, html);
    }

    /// <param name="workspaceName">The workspace's name, as its owner wrote it.</param>
    /// <param name="expiresAt">When the invitation stops working.</param>
    /// <param name="link">The acceptance page, with the token in its fragment.</param>
    public static OperationalEmailMessage Invitation(
        EmailAddress to,
        UserLanguage language,
        string workspaceName,
        DateTimeOffset expiresAt,
        Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);

        bool arabic = language == UserLanguage.Arabic;
        string expiry = EmailWords.Isolate(
            expiresAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

        (string text, string html) = EmailLayout.Compose(
            language,
            EmailLayout.SingleLine(workspaceName),
            arabic ? "تلقّيت دعوة" : "You have been invited",
            arabic
                ? [
                    "دُعيت للانضمام إلى مساحة العمل هذه على نبرة. افتح الدعوة، ثم سجّل الدخول بهذا العنوان أو أنشئ به حسابًا.",
                    "تنتهي صلاحية الدعوة في " + expiry + " بالتوقيت العالمي. إن لم تكن تتوقعها، فتجاهل هذه الرسالة.",
                ]
                : [
                    "You have been invited to join this workspace on Nabra. Open the invitation, then sign in with this address or create an account with it.",
                    "The invitation expires on " + expiry + " (UTC). If you were not expecting it, you can ignore this email.",
                ],
            (arabic ? "فتح الدعوة" : "Open invitation", link),
            arabic
                ? "تصلك هذه الرسالة لأن أحد أعضاء مساحة العمل هذه دعا هذا العنوان إلى نبرة."
                : "You are receiving this email because a member of this workspace invited this address to Nabra.");

        return new OperationalEmailMessage(to, Subject(AccountEmailKind.Invitation, language), text, html);
    }

    private static string EnglishMinutes(int minutes) =>
        minutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");

    /// <summary>Arabic counted nouns: دقيقة واحدة، دقيقتين، 3–10 دقائق، 11 فما فوق دقيقة.</summary>
    private static string ArabicMinutes(int minutes) => minutes switch
    {
        1 => "دقيقة واحدة",
        2 => "دقيقتين",
        <= 10 => string.Create(CultureInfo.InvariantCulture, $"{minutes} دقائق"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{minutes} دقيقة"),
    };
}
