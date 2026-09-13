namespace OmniAI.Domain.WebChat;

/// <summary>
/// The line a visitor sees before anybody has said anything.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is not a message, and it is never stored as one.</strong> No
/// conversation exists when it is shown, nobody wrote it to this visitor, and the
/// AI did not produce it. Writing it into the transcript would put words in an
/// agent's mouth, make a greeting appear in the admin's conversation history as
/// though it had been said, and count it against the context budget of a
/// conversation that has not started. It is page furniture, held on the endpoint's
/// configuration.
/// </para>
/// <para>
/// Plain text, bounded at 300 characters. Long enough for "Hi — ask us anything
/// about delivery, returns or your order" and short enough that it cannot become
/// a wall of marketing copy in a small panel.
/// </para>
/// </remarks>
public sealed record WebChatGreeting
{
    public const int MaxLength = 300;

    private WebChatGreeting(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Reads a greeting, treating blank as "no greeting at all".
    /// </summary>
    /// <remarks>
    /// Null rather than an empty greeting, because the widget renders nothing
    /// rather than an empty bubble, and the two need to be distinguishable.
    /// </remarks>
    public static WebChatGreeting? CreateOrNone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalised = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (normalised.Length == 0)
        {
            return null;
        }

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"A greeting must be at most {MaxLength} characters; the supplied greeting has "
                + $"{normalised.Length}. It is not truncated.");
        }

        foreach (char character in normalised)
        {
            // Line feeds are ordinary formatting in a two-line greeting. Nothing
            // else in the control range means anything here, and a stray one would
            // corrupt every log line and every rendering of it.
            if (char.IsControl(character) && character is not '\n')
            {
                throw new DomainValidationException(
                    "A greeting must not contain control characters other than line feed.");
            }
        }

        return new WebChatGreeting(normalised);
    }

    public override string ToString() => Value;
}
