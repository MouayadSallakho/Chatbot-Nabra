using System.Globalization;

namespace OmniAI.Domain.Users;

/// <summary>
/// The language a person reads what Nabra sends them in.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A communication preference, not the interface's language.</strong>
/// The admin application takes its own language from the browser's stored
/// choice (D22). This value decides what the server writes to the person:
/// emails today. One per person, not per workspace (Phase 37.3, O-3).
/// </para>
/// <para>
/// <strong>Two values and an absence.</strong> Stored as <c>en</c> or
/// <c>ar</c>; an account that has not chosen has none, and everything the server
/// writes to it is in English (<see cref="OrDefault"/>).
/// </para>
/// </remarks>
public enum UserLanguage
{
    English = 1,

    Arabic = 2,
}

/// <summary>Reading and writing <see cref="UserLanguage"/> as the two codes the API and the database use.</summary>
public static class UserLanguages
{
    public const string EnglishCode = "en";

    public const string ArabicCode = "ar";

    /// <summary>The longest stored form, for the column length.</summary>
    public const int MaxCodeLength = 8;

    /// <summary>The language of an account that has not chosen one.</summary>
    public static UserLanguage OrDefault(UserLanguage? language) => language ?? UserLanguage.English;

    public static string Code(UserLanguage language) => language switch
    {
        UserLanguage.English => EnglishCode,
        UserLanguage.Arabic => ArabicCode,
        _ => throw new ArgumentOutOfRangeException(nameof(language), language, "Unknown language."),
    };

    public static string? Code(UserLanguage? language) => language is UserLanguage known ? Code(known) : null;

    /// <summary>
    /// Exactly <c>en</c> or <c>ar</c>, as a request or a stored row carries it.
    /// </summary>
    /// <remarks>
    /// Exact and lower-case on purpose: the API documents two codes, and a value
    /// like <c>AR</c> or <c>ar-SA</c> in a JSON field is a client mistake worth
    /// refusing rather than guessing at. Headers are parsed by
    /// <see cref="FromAcceptLanguage"/>, which is forgiving because browsers write
    /// them.
    /// </remarks>
    public static bool TryParse(string? code, out UserLanguage language)
    {
        switch (code)
        {
            case EnglishCode:
                language = UserLanguage.English;
                return true;

            case ArabicCode:
                language = UserLanguage.Arabic;
                return true;

            default:
                language = default;
                return false;
        }
    }

    /// <summary>A stored code, or nothing when the row holds none or one this version does not know.</summary>
    public static UserLanguage? FromStored(string? code) =>
        TryParse(code, out UserLanguage language) ? language : null;

    /// <summary>
    /// The first supported language in an <c>Accept-Language</c> header, by q-value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the primary subtag counts, so <c>ar-SA;q=0.9</c> is Arabic. Ties keep
    /// the header's order. A range with <c>q=0</c> means "not this one" and is
    /// skipped, and <c>*</c> names nothing in particular, so it decides nothing.
    /// </para>
    /// <para>
    /// A malformed entry is skipped rather than failing the whole header: the
    /// header is advice, and the answer for advice nobody can read is "none".
    /// </para>
    /// </remarks>
    public static UserLanguage? FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        UserLanguage? best = null;
        double bestQuality = 0;

        // Bounded: a header is client input, and nobody's browser sends more.
        string[] ranges = header.Split(',', 32, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (string range in ranges)
        {
            string[] parts = range.Split(';', StringSplitOptions.TrimEntries);
            string tag = parts[0];
            double quality = 1;

            for (int index = 1; index < parts.Length; index++)
            {
                if (parts[index].StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parts[index][2..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double parsed))
                {
                    quality = Math.Clamp(parsed, 0, 1);
                }
            }

            if (quality <= 0 || quality <= bestQuality)
            {
                continue;
            }

            int dash = tag.IndexOf('-', StringComparison.Ordinal);
            string primary = dash < 0 ? tag : tag[..dash];

            UserLanguage? candidate = primary.ToUpperInvariant() switch
            {
                "EN" => UserLanguage.English,
                "AR" => UserLanguage.Arabic,
                _ => null,
            };

            if (candidate is not null)
            {
                best = candidate;
                bestQuality = quality;
            }
        }

        return best;
    }
}
