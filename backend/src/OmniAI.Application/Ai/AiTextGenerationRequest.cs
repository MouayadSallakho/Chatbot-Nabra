namespace OmniAI.Application.Ai;

/// <summary>
/// Everything an AI text generator is given for one generation call.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Three slots, and keeping them apart is the whole point.</strong>
/// <see cref="Instructions"/> is the privileged layer: what the business
/// configured this agent to be and do. <see cref="Turns"/> is conversational
/// content: what people said. <see cref="Grounding"/> is reference material the
/// answer may draw on, and is the least trusted of the three. They are separate
/// properties of separate types, so there is no single untyped prompt string in
/// which the boundaries between them could be lost, and no code path that turns
/// one into another.
/// </para>
/// <para>
/// <strong>Nothing here concatenates.</strong> This type never produces
/// <c>instructions + "\n" + text</c>, and neither should any caller. A provider
/// adapter translates these two slots into whatever its API expects -- a system
/// message and a message array, an instructions field and an input list,
/// something else again -- and that translation is the adapter's job precisely
/// because each provider does it differently. Flattening here would decide it
/// once, wrongly, for all of them, and would erase the privilege boundary before
/// the adapter ever saw it.
/// </para>
/// <para>
/// <strong>What is deliberately absent.</strong> No model name, no provider name,
/// no temperature, no top-p, no maximum output tokens, no reasoning effort, no
/// response format, no tool definitions, no seed. Those are provider and
/// configuration concerns and belong to the phase that integrates a real
/// provider, where a live API can say which of them exist and behave stably.
/// Putting a model name here would also make one vendor's catalogue part of this
/// platform's internal vocabulary.
/// </para>
/// <para>
/// A class rather than a record: it holds a list, and a record's generated
/// equality would compare that list by reference, so two requests with identical
/// content would report themselves unequal. Nothing needs to compare requests, and
/// an equality operator that quietly gives the wrong answer is worse than none.
/// </para>
/// </remarks>
public sealed class AiTextGenerationRequest
{
    private AiTextGenerationRequest(
        string instructions,
        IReadOnlyList<AiTurn> turns,
        IReadOnlyList<GroundingItem> grounding)
    {
        Instructions = instructions;
        Turns = turns;
        Grounding = grounding;
    }

    /// <summary>
    /// The agent's configured instructions, exactly as the business wrote them.
    /// </summary>
    /// <remarks>
    /// A plain string rather than the Domain's <c>AgentInstructions</c>, so this
    /// contract stays usable by anything that needs text generated and does not
    /// depend on the AI agent aggregate. The caller is responsible for taking this
    /// value from a source it has already established the caller is entitled to --
    /// which, for agent preview, means after tenant ownership has been resolved.
    /// <para>
    /// <strong>Nothing retrieved is ever added to this.</strong> Reference
    /// material travels in <see cref="Grounding"/>, and an adapter that merged the
    /// two would erase the only distinction that stops a knowledge document from
    /// being able to reprogram an agent.
    /// </para>
    /// </remarks>
    public string Instructions { get; }

    /// <summary>
    /// The conversation so far, oldest first.
    /// </summary>
    /// <remarks>
    /// Ordered, because a conversation read out of order is a different
    /// conversation. Phase 8's preview supplies exactly one user turn; the list
    /// exists so a conversation runtime can replay a history through the same
    /// contract without it having to be reopened.
    /// </remarks>
    public IReadOnlyList<AiTurn> Turns { get; }

    /// <summary>
    /// Reference material this answer may draw on. Untrusted, and usually empty.
    /// </summary>
    /// <remarks>
    /// A separate collection rather than extra turns, because it is not part of
    /// the conversation: nobody said it, it has no position in time, and inserting
    /// it among the turns would make the transcript a record of things that were
    /// never exchanged. An adapter renders it through a channel that carries no
    /// authority -- see the OpenAI adapter for how, and
    /// <see cref="GroundingPolicy"/> for what the model is told about it.
    /// </remarks>
    public IReadOnlyList<GroundingItem> Grounding { get; }

    /// <summary>
    /// Builds a generation request.
    /// </summary>
    /// <param name="instructions">The agent's configured instructions.</param>
    /// <param name="turns">The conversation, oldest first. At least one turn.</param>
    /// <param name="grounding">
    /// Reference material, or nothing. Optional so that every caller which has no
    /// knowledge to supply -- agent preview, and any agent with none assigned --
    /// stays exactly as it was.
    /// </param>
    /// <remarks>
    /// The turns are copied, so a caller cannot alter the request after handing it
    /// over -- which matters when the recipient is an adapter that may hold the
    /// request across an await while it talks to a network service.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The instructions are blank, or the turns are empty or contain a null. These
    /// are programming errors in a caller, not something an end user can cause: a
    /// blank instruction set cannot come out of the Domain, and a request with
    /// nothing to answer cannot be built from any endpoint. They are argument
    /// failures rather than
    /// <see cref="AiTurnValidationException"/> for that reason -- the caller's
    /// input was already validated where a person could see the message.
    /// </exception>
    public static AiTextGenerationRequest Create(
        string instructions,
        IReadOnlyList<AiTurn> turns,
        IReadOnlyList<GroundingItem>? grounding = null)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            throw new ArgumentException(
                "Generation requires the agent's instructions.", nameof(instructions));
        }

        ArgumentNullException.ThrowIfNull(turns);

        if (turns.Count == 0)
        {
            throw new ArgumentException("Generation requires at least one turn.", nameof(turns));
        }

        AiTurn[] copy = [.. turns];

        if (Array.IndexOf(copy, null) >= 0)
        {
            throw new ArgumentException("A turn must not be null.", nameof(turns));
        }

        GroundingItem[] items = grounding is null ? [] : [.. grounding];

        if (Array.IndexOf(items, null) >= 0)
        {
            throw new ArgumentException("A grounding item must not be null.", nameof(grounding));
        }

        return new AiTextGenerationRequest(instructions, copy, items);
    }
}
