namespace OmniAI.Domain.AiAgents;

/// <summary>
/// What an AI agent is for, in the business's own words -- its purpose, scope,
/// tone and the things it should and should not do.
/// </summary>
/// <remarks>
/// <b>This is platform configuration, not a provider payload.</b> It is not an
/// OpenAI system message, not a role/content message array, not a prompt
/// template and not a serialised request body. It carries no model name, no
/// provider id, no temperature, no top-p, no token limit and no JSON. It is one
/// piece of text that a business author wrote, and how any particular provider
/// is eventually asked to honour it -- which message role it lands in, how it is
/// combined with retrieved context or tool definitions, how tokens are
/// budgeted -- is that provider adapter's concern, in a later phase.
/// <para>
/// A validated type rather than a bare <see cref="string"/>, for the same reason
/// as every other value object here: the rule is enforced once, at construction,
/// so an invalid value cannot exist.
/// </para>
/// </remarks>
public sealed record AgentInstructions
{
    /// <summary>
    /// Maximum length, in UTF-16 code units, measured after trimming.
    /// </summary>
    /// <remarks>
    /// A <b>platform resource-safety bound, deliberately not any provider's
    /// context limit.</b> Provider limits are measured in tokens rather than
    /// characters, differ per model, and change without notice, so encoding one
    /// here would put a vendor's current pricing decision inside the Domain and
    /// would be wrong within months.
    /// <para>
    /// 20,000 characters is roughly 40 KB. It is far above any realistic
    /// hand-written instruction set -- a thorough one runs to a few thousand
    /// characters -- while bounding storage, log and render cost, and bounding the
    /// size of a value the platform will read on every single agent turn. Whether
    /// a given set of instructions actually fits a chosen model's context, and
    /// what to do when it does not, is a provider-adapter concern.
    /// </para>
    /// </remarks>
    public const int MaxLength = 20_000;

    private AgentInstructions(string value) => Value = value;

    /// <summary>The normalised instruction text.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and normalises instruction text.
    /// </summary>
    /// <remarks>
    /// Only surrounding whitespace is trimmed. Everything inside is preserved
    /// exactly -- line breaks, blank lines, indentation, list markers -- because
    /// interior formatting carries meaning in instructions in a way it does not
    /// in a name: an author who writes numbered rules on separate lines means
    /// them to stay on separate lines, and collapsing that would change what the
    /// agent was told to do.
    /// </remarks>
    /// <exception cref="DomainValidationException">
    /// The value is null, blank, longer than <see cref="MaxLength"/>, or contains
    /// a control character other than tab, carriage return or line feed.
    /// </exception>
    public static AgentInstructions Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException("AI agent instructions are required.")
            {
                Code = "ai-agents.instructions.required",
            };
        }

        string normalised = value.Trim();

        if (normalised.Length > MaxLength)
        {
            throw new DomainValidationException(
                $"AI agent instructions must be at most {MaxLength} characters; " +
                $"the supplied instructions have {normalised.Length}. They are not truncated.")
            {
                Code = "ai-agents.instructions.too_long",
            };
        }

        foreach (char character in normalised)
        {
            // Line breaks and tabs are ordinary instruction formatting. Other
            // control characters are not: they carry no meaning here and would
            // corrupt logs, editors and rendered output.
            if (char.IsControl(character) && character is not ('\n' or '\r' or '\t'))
            {
                throw new DomainValidationException(
                    "AI agent instructions must not contain control characters other than " +
                    "tab, carriage return and line feed.")
                {
                    Code = "ai-agents.instructions.control_characters",
                };
            }
        }

        return new AgentInstructions(normalised);
    }

    public override string ToString() => Value;
}
