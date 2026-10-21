using System.Globalization;
using System.Net;
using MimeKit;

namespace OmniAI.Infrastructure.Notifications.Email;

/// <summary>How the SMTP connection is secured.</summary>
public enum SmtpSecurity
{
    /// <summary>Upgrade with STARTTLS, and refuse to continue if the server cannot.</summary>
    StartTls = 1,

    /// <summary>TLS from the first byte (port 465).</summary>
    SslOnConnect = 2,

    /// <summary>Plain text. Refused for any host but loopback.</summary>
    None = 3,
}

/// <summary>
/// Where operational email is sent from, and how.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Off unless a host is set.</strong> A deployment with no
/// <c>Email:Smtp:Host</c> sends nothing and says so; in-app notifications work
/// either way.
/// </para>
/// <para>
/// <strong>Secrets come from the environment only.</strong> There is no default
/// password and none in any committed settings file. The password is read from
/// configuration (for example <c>Email__Smtp__Password</c>) and handed to the SMTP
/// client; it is never logged, never stored in the database and never sent to a
/// browser.
/// </para>
/// <para>
/// <strong>Validated at startup.</strong> A plain-text connection to a remote
/// host, a sender address with a line break in it, a malformed or non-HTTPS
/// application address — each stops the process from starting rather than
/// surfacing the first time somebody is waiting for an email.
/// </para>
/// </remarks>
public sealed class OperationalEmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Set to exactly "1" to let Development send to a non-loopback SMTP host.</summary>
    public const string LiveEmailOptInVariable = "OMNIAI_LIVE_EMAIL";

    public const string LiveEmailOptInValue = "1";

    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>
    /// The public address of the admin application, which every link in an email is built from.
    /// </summary>
    public string? AppBaseUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Smtp.Host);

    /// <summary>The validated application address. Only meaningful when configured.</summary>
    public Uri? AppBaseUri { get; private set; }

    public bool TargetsLoopback => IsLoopback(Smtp.Host);

    public void Validate()
    {
        if (!IsConfigured)
        {
            return;
        }

        string host = Smtp.Host!.Trim();

        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw Invalid("Smtp:Host must be a host name or IP address.");
        }

        if (Smtp.Port is < 1 or > 65535)
        {
            throw Invalid("Smtp:Port must be between 1 and 65535.");
        }

        if (!Enum.IsDefined(Smtp.Security))
        {
            throw Invalid("Smtp:Security must be StartTls, SslOnConnect or None.");
        }

        // An unencrypted connection is acceptable to a sink on this machine and
        // nowhere else: credentials and message content would cross a network in
        // the clear.
        if (Smtp.Security == SmtpSecurity.None && !IsLoopback(host))
        {
            throw Invalid("Smtp:Security None is only allowed for a loopback host. Use StartTls or SslOnConnect.");
        }

        if (string.IsNullOrEmpty(Smtp.Username) != string.IsNullOrEmpty(Smtp.Password))
        {
            throw Invalid("Smtp:Username and Smtp:Password must be set together, or not at all.");
        }

        if (Smtp.Timeout < TimeSpan.FromSeconds(1) || Smtp.Timeout > TimeSpan.FromMinutes(2))
        {
            throw Invalid("Smtp:Timeout must be between one second and two minutes.");
        }

        if (HasLineBreak(Smtp.FromName) || HasLineBreak(Smtp.FromAddress))
        {
            throw Invalid("Smtp:FromName and Smtp:FromAddress must be single lines.");
        }

        if (string.IsNullOrWhiteSpace(Smtp.FromAddress)
            || !MailboxAddress.TryParse(Smtp.FromAddress, out MailboxAddress? from)
            || !string.Equals(from.Address, Smtp.FromAddress.Trim(), StringComparison.OrdinalIgnoreCase)
            || !from.Address.Contains('@', StringComparison.Ordinal))
        {
            throw Invalid("Smtp:FromAddress must be one plain email address.");
        }

        AppBaseUri = ValidateAppBaseUrl(AppBaseUrl);
    }

    private static Uri ValidateAppBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri))
        {
            throw Invalid("AppBaseUrl must be the absolute address of the admin application.");
        }

        bool secure = uri.Scheme == Uri.UriSchemeHttps;
        bool localHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;

        if (!secure && !localHttp)
        {
            throw Invalid("AppBaseUrl must use https (http is allowed only for a loopback address).");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Invalid("AppBaseUrl must not carry credentials, a query or a fragment.");
        }

        return uri;
    }

    internal static bool IsLoopback(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        string trimmed = host.Trim().Trim('[', ']');

        return string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(trimmed, out IPAddress? address) && IPAddress.IsLoopback(address));
    }

    private static bool HasLineBreak(string? value) =>
        value is not null && value.Any(character => character is '\r' or '\n' || char.IsControl(character));

    private static InvalidOperationException Invalid(string message) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{SectionName}:{message}"));
}

/// <summary>The SMTP server operational email is handed to.</summary>
public sealed class SmtpSettings
{
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? Username { get; set; }

    /// <summary>From the environment only. Never logged, stored or returned.</summary>
    public string? Password { get; set; }

    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "Nabra";

    /// <summary>How long connecting, signing in and sending may each take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
