namespace OmniAI.Application.Conversations;

/// <summary>Conservative hints from the first customer text. Unknown input stays in the general inbox.</summary>
public static class RoutingLanguage
{
    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "hi", "hey", "thanks", "please", "could", "would", "what", "when", "where", "how",
        "the", "my", "your", "can", "have", "order", "help", "refund", "delivery", "support",
    };

    public static string? Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int arabic = 0, latin = 0;
        foreach (char c in text)
        {
            if (c is >= '\u0600' and <= '\u06ff' && char.IsLetter(c))
            {
                arabic++;
            }
            else if (char.IsAsciiLetter(c))
            {
                latin++;
            }
        }
        if (arabic > latin && arabic >= 2)
        {
            return "ar";
        }
        if (latin <= arabic)
        {
            return null;
        }
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && char.IsAsciiLetter(text[i]))
            {
                continue;
            }
            if (i > start && EnglishWords.Contains(text[start..i]))
            {
                return "en";
            }
            start = i + 1;
        }
        return null;
    }
}
