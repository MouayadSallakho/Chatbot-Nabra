using System.Globalization;
using System.Text;

namespace OmniAI.Infrastructure.Ai.StandIn;

/// <summary>
/// How the local AI stand-in reads text: words, and which script dominates.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately simple and fully deterministic. A word is a run of letters and
/// digits; English is lower-cased, and Arabic is lightly normalised (diacritics
/// and tatweel removed, alef forms unified, final ى read as ي and ة as ه, and a
/// leading «ال» dropped) so «الشحن» and «شحن» are the same word. That is enough
/// for a question to find the passages that share its words, which is all the
/// stand-in promises.
/// </para>
/// <para>
/// These are word counts, not tokens. The ledger records them in the token
/// columns so a local call can be priced at zero; no tokeniser is involved.
/// </para>
/// </remarks>
internal static class LocalStandInText
{
    /// <summary>The normalised words of <paramref name="text"/>, in order.</summary>
    public static List<string> Words(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> words = [];
        StringBuilder current = new();

        foreach (char character in text)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category == UnicodeCategory.NonSpacingMark || character == 'ـ')
            {
                // Arabic diacritics and tatweel decorate a word; they do not end it.
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                current.Append(Fold(character));

                continue;
            }

            Flush(current, words);
        }

        Flush(current, words);

        return words;
    }

    /// <summary>How many words <paramref name="text"/> has.</summary>
    public static int CountWords(string text) => Words(text).Count;

    /// <summary>
    /// Whether <paramref name="text"/> is written mostly in Arabic letters.
    /// </summary>
    /// <remarks>
    /// Letters are compared, not characters, so digits, punctuation and a
    /// <c>[[fail]]</c> marker do not decide the language of an Arabic message.
    /// </remarks>
    public static bool IsArabic(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int arabic = 0;
        int other = 0;

        foreach (char character in text)
        {
            if (!char.IsLetter(character))
            {
                continue;
            }

            if (IsArabicLetter(character))
            {
                arabic++;
            }
            else
            {
                other++;
            }
        }

        return arabic > other;
    }

    private static bool IsArabicLetter(char character) =>
        character is (>= '؀' and <= 'ۿ')
            or (>= 'ݐ' and <= 'ݿ')
            or (>= 'ࢠ' and <= 'ࣿ')
            or (>= 'ﭐ' and <= '﷿')
            or (>= 'ﹰ' and <= '﻿');

    private static char Fold(char character) => character switch
    {
        'آ' or 'أ' or 'إ' or 'ٱ' => 'ا', // آ أ إ ٱ -> ا
        'ى' => 'ي', // ى -> ي
        'ة' => 'ه', // ة -> ه
        _ => char.ToLowerInvariant(character),
    };

    private static void Flush(StringBuilder current, List<string> words)
    {
        if (current.Length == 0)
        {
            return;
        }

        string word = current.ToString();

        current.Clear();

        // «ال» is the definite article, written joined to its noun.
        if (word.Length > 3 && word.StartsWith("ال", StringComparison.Ordinal))
        {
            word = word[2..];
        }

        words.Add(word);
    }
}
