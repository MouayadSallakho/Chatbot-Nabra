using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Identity;
using OmniAI.Application.Accounts;
using OmniAI.Domain.Users;

namespace OmniAI.Api.Auth;

/// <summary>
/// The account endpoints' refusals, each with a stable <c>code</c> (Phase 37.3,
/// D-2, row 6b; <c>docs/architecture/problem-codes.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The code is the contract; the detail stays English.</strong> The client
/// words a refusal by its code. Statuses and details of the refusals that existed
/// before Phase 37.3 are unchanged; only the code was added.
/// </para>
/// <para>
/// <strong>No code says whether an address has an account.</strong> An unknown
/// address and a wrong password are both <c>auth.credentials.invalid</c>, and
/// throttling counts unknown addresses too.
/// </para>
/// </remarks>
internal static partial class AccountProblems
{
    public const string CredentialsInvalid = "auth.credentials.invalid";
    public const string RegistrationIncomplete = "auth.registration.incomplete";
    public const string RegistrationRejected = "auth.registration.rejected";
    public const string EmailTaken = "auth.email.taken";
    public const string EmailInvalid = "auth.email.invalid";
    public const string PasswordPrefix = "auth.password.";
    public const string ResetInvalid = "auth.reset.invalid";
    public const string Throttled = "auth.throttled";
    public const string ThrottleUnavailable = "auth.throttle.unavailable";
    public const string LanguageUnsupported = "language.unsupported";

    /// <summary>The code for one Identity error.</summary>
    public static string CodeFor(IdentityError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code switch
        {
            "DuplicateEmail" or "DuplicateUserName" => EmailTaken,
            "InvalidEmail" or "InvalidUserName" => EmailInvalid,
            "InvalidToken" => ResetInvalid,
            string code when code.StartsWith("Password", StringComparison.Ordinal) && code.Length > "Password".Length =>
                PasswordPrefix + Snake(code["Password".Length..]),
            _ => RegistrationRejected,
        };
    }

    /// <summary>A refusal carrying Identity's own sentences and the code of each error.</summary>
    /// <remarks>
    /// <c>code</c> is the first error's; <c>codes</c> lists all of them, in order, for
    /// a form that shows each rule next to the field.
    /// </remarks>
    public static IResult FromIdentity(string title, IEnumerable<IdentityError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        IdentityError[] all = [.. errors];
        string[] codes = [.. all.Select(CodeFor).Distinct(StringComparer.Ordinal)];

        return Results.Problem(
            title: title,
            detail: string.Join(" ", all.Select(error => error.Description)),
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = codes.Length > 0 ? codes[0] : RegistrationRejected,
                ["codes"] = codes,
            });
    }

    public static IResult Problem(int status, string title, string detail, string code) =>
        Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>429 with <c>Retry-After</c>, naming no limit, window or address.</summary>
    public static IResult TooMany(HttpContext http, AccountProtectionDecision decision)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(decision);

        http.Response.Headers.RetryAfter = Math
            .Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);

        return Problem(
            StatusCodes.Status429TooManyRequests,
            "Too many requests",
            "Too many attempts. Wait a while, then try again.",
            Throttled);
    }

    /// <summary>
    /// 503 when the limiter cannot decide. Refused rather than served unlimited; the
    /// log, not the caller, gets the reason.
    /// </summary>
    public static IResult Unavailable(ILogger logger, Exception reason)
    {
        LogUnprotected(logger, reason);

        return Problem(
            StatusCodes.Status503ServiceUnavailable,
            "Temporarily unavailable",
            "This cannot be done right now. Try again in a few minutes.",
            ThrottleUnavailable);
    }

    /// <summary>An optional <c>language</c>: absent is fine, <c>en</c> and <c>ar</c> are, anything else is refused.</summary>
    public static bool TryReadLanguage(string? value, out UserLanguage? language, out IResult? refusal)
    {
        refusal = null;
        language = null;

        if (value is null)
        {
            return true;
        }

        if (UserLanguages.TryParse(value, out UserLanguage parsed))
        {
            language = parsed;

            return true;
        }

        refusal = Problem(
            StatusCodes.Status400BadRequest,
            "Language not supported",
            "The language must be \"en\" or \"ar\".",
            LanguageUnsupported);

        return false;
    }

    /// <summary>The language a request asked for, else its <c>Accept-Language</c>, else none.</summary>
    public static UserLanguage? Preferred(HttpContext http, UserLanguage? requested)
    {
        ArgumentNullException.ThrowIfNull(http);

        return requested ?? UserLanguages.FromAcceptLanguage(http.Request.Headers.AcceptLanguage.ToString());
    }

    private static string Snake(string pascal)
    {
        StringBuilder snake = new(pascal.Length + 4);

        foreach (char character in pascal)
        {
            if (char.IsUpper(character) && snake.Length > 0)
            {
                snake.Append('_');
            }

            snake.Append(char.ToLowerInvariant(character));
        }

        return snake.ToString();
    }

    [LoggerMessage(EventId = 4340, Level = LogLevel.Warning, Message = "An account request was refused because the account limiter could not decide.")]
    private static partial void LogUnprotected(ILogger logger, Exception reason);

    [LoggerMessage(EventId = 4341, Level = LogLevel.Warning, Message = "A successful sign-in could not clear its address's failures on its network; they will expire on their own.")]
    public static partial void LogNotCleared(ILogger logger, Exception reason);
}
