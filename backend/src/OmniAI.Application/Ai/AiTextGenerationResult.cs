namespace OmniAI.Application.Ai;

/// <summary>
/// What an AI text generator produced.
/// </summary>
/// <param name="Text">The generated reply, trimmed at the ends.</param>
/// <remarks>
/// <para>
/// One field, because one field is what Phase 8 can actually justify. No model
/// name, no provider name, no token counts, no cost, no finish reason, no tool
/// calls, no raw provider payload and no request id: none of those has been
/// observed coming out of a real provider yet, so every one of them would be a
/// guess at a shape, and a guess baked into a contract is harder to remove than
/// to add. The phase that integrates a real provider will see which of them exist
/// and are stable, and can add exactly those.
/// </para>
/// <para>
/// <strong>An unusable value cannot be wrapped in this type.</strong>
/// <see cref="Create"/> refuses blank text, so a provider adapter cannot hand
/// back a successful-looking result carrying nothing. That makes the boundary the
/// type itself rather than a check every caller has to remember, and it fails at
/// the point the bad value was produced -- inside the adapter, where the cause
/// is -- rather than further up where it is only a symptom.
/// </para>
/// </remarks>
public sealed record AiTextGenerationResult
{
    private AiTextGenerationResult(string text) => Text = text;

    /// <summary>The generated reply. Never null, empty or whitespace-only.</summary>
    public string Text { get; }

    /// <summary>
    /// Wraps generated text as a result.
    /// </summary>
    /// <remarks>
    /// Surrounding whitespace is trimmed and the interior is left alone, matching
    /// how the platform treats every other piece of authored text: a model that
    /// ends its answer with a newline has still given the same answer, whereas its
    /// paragraph breaks and lists are the answer's structure.
    /// <para>
    /// Emptiness is judged after trimming, so a reply consisting only of spaces or
    /// newlines is rejected rather than stored as a technically non-empty string.
    /// </para>
    /// </remarks>
    /// <exception cref="AiResponseInvalidException">
    /// The text is null, empty, or whitespace only. That is an upstream
    /// contract breach, not a caller error -- see the exception's own notes.
    /// </exception>
    public static AiTextGenerationResult Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiResponseInvalidException();
        }

        return new AiTextGenerationResult(text.Trim());
    }
}
