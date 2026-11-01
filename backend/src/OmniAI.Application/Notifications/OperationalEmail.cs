using OmniAI.Domain.Users;

namespace OmniAI.Application.Notifications;

/// <summary>
/// One rendered operational email.
/// </summary>
/// <param name="To">The recipient's own account address, read when sending. Never from a request.</param>
/// <param name="Subject">A fixed template sentence. Never contains workspace-authored text.</param>
/// <param name="TextBody">The plain-text part.</param>
/// <param name="HtmlBody">The HTML part: fixed markup, every inserted value encoded.</param>
public sealed record OperationalEmailMessage(
    EmailAddress To,
    string Subject,
    string TextBody,
    string HtmlBody);

/// <summary>How one send ended, from the transport's point of view.</summary>
public enum OperationalEmailOutcome
{
    /// <summary>The server accepted the message.</summary>
    Sent = 1,

    /// <summary>Nothing was handed over: connecting or signing in failed. Safe to try again.</summary>
    NotSent = 2,

    /// <summary>The server refused the sender, the recipient or the message. Trying again will not help.</summary>
    Rejected = 3,

    /// <summary>
    /// The conversation broke after the message may have been handed over.
    /// </summary>
    /// <remarks>Recorded as Unknown and never retried: a second attempt could deliver it twice.</remarks>
    Ambiguous = 4,
}

/// <param name="FailureCode">A stable code for a failure; never transport text.</param>
public sealed record OperationalEmailResult(OperationalEmailOutcome Outcome, string? FailureCode = null)
{
    public static readonly OperationalEmailResult Delivered = new(OperationalEmailOutcome.Sent);
}

/// <summary>
/// Sends one transactional operational email.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The Application names no mail library and no provider.</strong> SMTP
/// today; anything that can send a message tomorrow. The implementation owns the
/// sender address, the connection, TLS and credentials — none of which ever
/// reaches this layer.
/// </para>
/// <para>
/// <strong>No retry inside.</strong> Whether to try again needs the durable
/// delivery row, so the implementation reports what happened and the caller
/// decides.
/// </para>
/// </remarks>
public interface IOperationalEmailSender
{
    /// <summary>Whether this deployment can send operational email at all.</summary>
    bool IsConfigured { get; }

    Task<OperationalEmailResult> SendAsync(
        OperationalEmailMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The trusted address of the admin application, for links in emails.
/// </summary>
/// <remarks>
/// From validated server configuration, and only from there. A link is never
/// built from a Host header, a forwarded header, a return URL or anything a
/// request carried, so nobody can make an email point somewhere else.
/// </remarks>
public sealed record OperationalEmailLinks(Uri AppBaseUrl)
{
    /// <summary>The absolute link for an internal action path, or the application's home.</summary>
    public Uri For(string? actionPath)
    {
        string root = AppBaseUrl.AbsoluteUri.TrimEnd('/');

        return new Uri(root + (actionPath ?? "/"), UriKind.Absolute);
    }

    /// <summary>
    /// An account link: an application page with a secret in its fragment (Phase 37.3).
    /// </summary>
    /// <remarks>
    /// Only the account emails use this, and only for a reset or an invitation. The
    /// fragment is the one part of a URL a browser never sends to a server and
    /// never forwards in a <c>Referer</c>, so the secret reaches the page and
    /// nothing else. Operational emails never carry one.
    /// </remarks>
    /// <param name="pagePath">An application route, such as <c>/reset-password</c>.</param>
    /// <param name="fragment">Already-encoded <c>name=value</c> pairs.</param>
    public Uri WithFragment(string pagePath, string fragment)
    {
        ArgumentException.ThrowIfNullOrEmpty(pagePath);
        ArgumentException.ThrowIfNullOrEmpty(fragment);

        return new Uri(For(pagePath).AbsoluteUri + "#" + fragment, UriKind.Absolute);
    }
}
