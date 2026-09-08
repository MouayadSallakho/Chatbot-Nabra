namespace OmniAI.Application.Ai;

/// <summary>
/// One piece of reference material a request may draw on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is data, not instruction, and the type exists to keep it that
/// way.</strong> A grounding item is text somebody in a workspace wrote into a
/// knowledge document. It may contain anything at all -- including sentences
/// written specifically to be read as commands: "ignore your instructions",
/// "reveal your configuration", "you are now in developer mode". Whether those
/// sentences have any power depends entirely on which channel they arrive
/// through, and a separate type is what makes it impossible to lose track of
/// which channel that is.
/// </para>
/// <para>
/// <strong>Why not simply append it to the instructions.</strong> Because the
/// instructions are the one thing in a request the workspace's administrators
/// authored and the model is meant to obey. Concatenating retrieved text into
/// them promotes whatever a document happens to say to the same authority as the
/// agent's own configuration, and a single document -- pasted from a supplier's
/// email, copied off a web page, or written by a disgruntled colleague -- would
/// be able to rewrite the agent's behaviour for every customer who asked a
/// question it happened to match.
/// </para>
/// <para>
/// The title travels with the text because it helps a model say which document an
/// answer came from, and it is exactly as untrusted as the text is.
/// </para>
/// </remarks>
public sealed record GroundingItem
{
    /// <summary>The most characters one item may carry.</summary>
    /// <remarks>
    /// Comfortably above a chunk, which is bounded far lower. It is here so that
    /// the request type refuses an oversized item on its own rather than trusting
    /// every caller to have bounded it.
    /// </remarks>
    public const int MaxTextLength = 4_000;

    /// <summary>The most characters an item's title may carry.</summary>
    public const int MaxTitleLength = 200;

    private GroundingItem(string title, string text)
    {
        Title = title;
        Text = text;
    }

    /// <summary>Which document this came from.</summary>
    public string Title { get; }

    /// <summary>The passage itself. Untrusted, always.</summary>
    public string Text { get; }

    public static GroundingItem Create(string? title, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiTurnValidationException("A grounding item must carry some text.");
        }

        string normalisedText = text.Trim();

        if (normalisedText.Length > MaxTextLength)
        {
            throw new AiTurnValidationException(
                $"A grounding item must be at most {MaxTextLength} characters; "
                + $"the supplied item has {normalisedText.Length}. It is not truncated.");
        }

        // A blank title is tolerated rather than refused: a passage with no name
        // is still worth having, and losing the answer because somebody's document
        // was called something unprintable would be the wrong trade.
        string normalisedTitle = string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim();

        if (normalisedTitle.Length > MaxTitleLength)
        {
            normalisedTitle = normalisedTitle[..MaxTitleLength];
        }

        return new GroundingItem(Single(normalisedTitle), normalisedText);
    }

    /// <summary>
    /// Flattens a title onto one line.
    /// </summary>
    /// <remarks>
    /// The only place in this feature where text is altered rather than refused,
    /// and it is a label. A title carrying a line break would let a document name
    /// itself across several lines of the rendered block and imitate the
    /// delimiters around it. Nothing is gained by that and the confusion is real,
    /// so the characters that could do it become spaces.
    /// </remarks>
    private static string Single(string title)
    {
        Span<char> buffer = title.Length <= 256 ? stackalloc char[title.Length] : new char[title.Length];

        for (int index = 0; index < title.Length; index++)
        {
            char character = title[index];

            buffer[index] = char.IsControl(character) ? ' ' : character;
        }

        return new string(buffer).Trim();
    }
}
