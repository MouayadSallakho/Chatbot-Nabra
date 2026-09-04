namespace OmniAI.Application.Ai;

/// <summary>
/// One piece of conversational text, and who said it.
/// </summary>
/// <remarks>
/// <para>
/// The smallest thing a text generator needs to be told about a conversation:
/// a role and some text. It is immutable and validated at construction, so an
/// unusable turn cannot exist to be passed around -- the same approach the Domain
/// value objects take, applied at the Application boundary where these rules
/// actually belong.
/// </para>
/// <para>
/// <strong>It is not a Domain <c>Message</c>, and not a provider SDK type.</strong>
/// A stored message is a business record with an identity, a channel, a direction
/// and an ingestion order; a turn is transient input to one generation call.
/// Reusing the aggregate here would drag persistence identity into a request that
/// stores nothing, and would tie the AI contract to the conversation model's
/// shape -- so a conversation runtime will later <em>project</em> messages into
/// turns rather than the two being the same type. In the other direction, this is
/// not modelled on any vendor's message object: no <c>content</c> parts array, no
/// tool-call fields, no provider-specific role strings.
/// </para>
/// <para>
/// <strong>Only <c>User</c> and <c>Assistant</c> can be built.</strong> There is
/// no public constructor and no factory that takes an arbitrary
/// <see cref="AiTurnRole"/>, so no call site can invent a privileged turn even by
/// accident. Agent instructions travel in their own slot on
/// <see cref="AiTextGenerationRequest"/>.
/// </para>
/// </remarks>
public sealed record AiTurn
{
    /// <summary>
    /// Maximum turn length, in UTF-16 code units, measured after trimming.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A platform resource bound, and explicitly not any provider's token
    /// or context limit.</strong> Provider limits are counted in tokens, differ
    /// per model, and change without notice; encoding one here would put a
    /// vendor's current pricing decision into this platform's request validation
    /// and would be wrong within months. What a chosen model can actually accept,
    /// and what to do when a request exceeds it, is a provider adapter's concern
    /// in a later phase.
    /// </para>
    /// <para>
    /// 10,000 characters is roughly 20 KB. It is far above any realistic customer
    /// message -- a long, detailed support enquiry runs to a couple of thousand
    /// characters -- while bounding the request body, the log line and the memory
    /// a single generation call can occupy. It sits below
    /// <c>AgentInstructions.MaxLength</c> on purpose: instructions are written
    /// once by a business author and read on every turn, whereas a turn is one
    /// person's message.
    /// </para>
    /// <para>
    /// Over-length text is rejected, never silently truncated. Truncating would
    /// send the model a question the person did not ask and return an answer to
    /// it as though nothing had happened.
    /// </para>
    /// </remarks>
    public const int MaxTextLength = 10_000;

    private AiTurn(AiTurnRole role, string text)
    {
        Role = role;
        Text = text;
    }

    /// <summary>Who produced this turn.</summary>
    public AiTurnRole Role { get; }

    /// <summary>The turn's text, trimmed at the ends and otherwise untouched.</summary>
    public string Text { get; }

    /// <summary>Builds a turn holding text the person typed.</summary>
    /// <exception cref="AiTurnValidationException">The text is unusable.</exception>
    public static AiTurn FromUser(string? text) => Create(AiTurnRole.User, text);

    /// <summary>Builds a turn holding text the agent previously produced.</summary>
    /// <exception cref="AiTurnValidationException">The text is unusable.</exception>
    public static AiTurn FromAssistant(string? text) => Create(AiTurnRole.Assistant, text);

    /// <summary>
    /// Validates and normalises turn text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only surrounding whitespace is trimmed. Everything inside survives exactly
    /// -- line breaks, blank lines, indentation, list markers -- because a person
    /// who lays a question out over several lines means it that way, and a
    /// pasted log or code snippet stops being the thing they pasted if its
    /// interior whitespace is collapsed. Trimming the ends only removes the stray
    /// newline a text box adds.
    /// </para>
    /// <para>
    /// The length is measured <em>after</em> trimming, so trailing whitespace
    /// cannot push an otherwise acceptable message over the bound.
    /// </para>
    /// <para>
    /// Control characters other than tab, carriage return and line feed are
    /// rejected, matching the rule <c>AgentInstructions</c> already applies: they
    /// carry no meaning in text a person wrote and corrupt logs, terminals and
    /// rendered output downstream.
    /// </para>
    /// </remarks>
    private static AiTurn Create(AiTurnRole role, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiTurnValidationException("A message is required and must not be blank.");
        }

        string normalised = text.Trim();

        if (normalised.Length > MaxTextLength)
        {
            throw new AiTurnValidationException(
                $"A message must be at most {MaxTextLength} characters; " +
                $"the supplied message has {normalised.Length}. It is not truncated.");
        }

        foreach (char character in normalised)
        {
            if (char.IsControl(character) && character is not ('\n' or '\r' or '\t'))
            {
                throw new AiTurnValidationException(
                    "A message must not contain control characters other than " +
                    "tab, carriage return and line feed.");
            }
        }

        return new AiTurn(role, normalised);
    }
}
