using OmniAI.Domain.Users;

namespace OmniAI.Api.WebChat;

/// <summary>
/// Reads the <c>lang</c> a public chat page sends (Phase 39.1, note 9).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Advice, never a refusal.</strong> <c>en</c> or <c>ar</c>, in any letter
/// case and with surrounding spaces ignored, picks that language's greeting.
/// Anything else, and no value at all, is read as "no language": the page gets the
/// fallback greeting, exactly the answer it got before Phase 39.1. A visitor's page
/// is never refused for the language it names, so this cannot become a way to tell
/// a valid key from an invalid one.
/// </para>
/// <para>
/// It only chooses between texts the response already carried one of: no new
/// member, no new header, and the rate limits, the origin check and the cache rules
/// of both reads are unchanged.
/// </para>
/// </remarks>
internal static class PageLanguage
{
    /// <summary>Longer than any tag a page would send; bounds the work on client input.</summary>
    private const int MaxLength = 16;

    public static UserLanguage? Read(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang) || lang.Length > MaxLength)
        {
            return null;
        }

        return lang.Trim().ToUpperInvariant() switch
        {
            "EN" => UserLanguage.English,
            "AR" => UserLanguage.Arabic,
            _ => null,
        };
    }
}
