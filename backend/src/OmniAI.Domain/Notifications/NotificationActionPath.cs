namespace OmniAI.Domain.Notifications;

/// <summary>
/// Where a notification's action leads: an internal route of the admin
/// application, and never anything else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Server-written and validated anyway.</strong> Every path comes from a
/// template in code, never from a provider, a webhook or a request. It is still
/// checked here, because a notification is rendered as a link and a link is the
/// shape an open redirect or a <c>javascript:</c> URL takes.
/// </para>
/// <para>
/// A path starts with one slash, is made of lower-case letters, digits,
/// hyphens and slashes, and has no scheme, host, query, fragment, backslash,
/// dot segment or doubled slash. <c>//evil.example</c>, <c>https://…</c> and
/// <c>javascript:…</c> are all refused by construction.
/// </para>
/// <para>
/// <strong>A link is not a permission.</strong> The destination page and its API
/// check the reader's current permissions exactly as they would for somebody who
/// typed the address.
/// </para>
/// </remarks>
public static class NotificationActionPath
{
    public const int MaxLength = 200;

    public static bool IsValid(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > MaxLength || path[0] != '/')
        {
            return false;
        }

        if (path.Length > 1 && (path[1] == '/' || path[^1] == '/'))
        {
            return false;
        }

        for (int index = 0; index < path.Length; index++)
        {
            char character = path[index];

            bool allowed = character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '/';

            if (!allowed || (character == '/' && index > 0 && path[index - 1] == '/'))
            {
                return false;
            }
        }

        return true;
    }

    /// <exception cref="DomainValidationException">The path is not an internal route.</exception>
    public static string Require(string path) =>
        IsValid(path) ? path : throw new DomainValidationException("A notification action must be an internal route.")
        {
            Code = "notifications.action.invalid",
        };
}
