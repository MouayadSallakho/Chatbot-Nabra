namespace OmniAI.Application.Ai;

/// <summary>
/// Who produced a turn in a conversation the platform sends to an AI text
/// generator.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two roles, and the omissions are the design.</strong> There is no
/// <c>System</c> and no <c>Developer</c> role here, because the privileged
/// instruction layer is not a turn: it is the agent's stored instructions,
/// carried in their own slot on <see cref="AiTextGenerationRequest"/>. Giving
/// conversational turns a system role would create a second, equally privileged
/// way to instruct the model -- one reachable from the same list that carries
/// text a customer typed. The separation is what makes it structurally
/// impossible for user text to be mistaken for platform instruction, rather than
/// a convention every future call site has to remember.
/// </para>
/// <para>
/// <c>Tool</c>, <c>Function</c>, <c>Image</c>, <c>Audio</c> and <c>Document</c>
/// are absent for a different reason: nothing in the platform produces them yet.
/// Adding them now would mean guessing at shapes no requirement has described,
/// and every provider adapter would have to translate cases that never occur.
/// </para>
/// <para>
/// This is the platform's own vocabulary, not any provider's. An adapter maps
/// these onto whatever its API calls the same idea; nothing here is named after,
/// or numbered to match, a vendor's wire format.
/// </para>
/// </remarks>
public enum AiTurnRole
{
    /// <summary>Text that came from the person the agent is talking to.</summary>
    /// <remarks>
    /// Treated as content, never as instruction, however it is worded. Text
    /// asking the model to disregard its instructions is still a user turn.
    /// </remarks>
    User = 1,

    /// <summary>Text the agent itself previously produced.</summary>
    /// <remarks>
    /// Not used by the Phase 8 preview, which sends a single user turn. It exists
    /// because a conversation runtime replays an alternating history, and a
    /// contract that could not express the agent's own past replies would have to
    /// be reopened the moment that arrives.
    /// </remarks>
    Assistant = 2,
}
