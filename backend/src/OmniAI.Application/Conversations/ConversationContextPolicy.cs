using OmniAI.Application.Ai;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.Conversations;

/// <summary>
/// How much of a conversation is replayed to the AI, and which part.
/// </summary>
/// <remarks>
/// <strong>A conversation is unbounded and a request is not.</strong> A chat that
/// has run for months holds thousands of messages; sending all of them on every
/// turn would grow the cost and latency of each reply without limit and would
/// eventually be refused by any provider. Some bound is mandatory, so the only
/// real question is who chooses it and on what basis.
/// <para>
/// <strong>This is a platform resource policy, not token counting.</strong>
/// Counting tokens means knowing the provider's tokeniser, which means the
/// Application knowing which provider it is talking to — exactly the coupling
/// <c>IAiTextGenerator</c> exists to prevent, and a number that changes when a
/// model changes. Whole messages and characters are provider-independent, stable,
/// and understandable to anybody reading a transcript. A provider that needs a
/// tighter bound than this is free to enforce its own; it has the knowledge to.
/// </para>
/// <para>
/// <strong>Two bounds, because either alone fails.</strong> A message count alone
/// lets forty long messages through and blows the budget; a character count alone
/// lets through a thousand one-word messages, each of which still costs
/// structure. Both are applied, and the tighter one wins on any given
/// conversation.
/// </para>
/// <para>
/// <strong>Newest wins, and only whole messages are dropped.</strong> The recent
/// exchange is what the next reply has to be consistent with, so the window is
/// taken from the end. Nothing is ever cut in half: a half-message changes what
/// somebody said, and a model asked to continue from a sentence that stops
/// mid-word will answer something nobody said either. Order is never touched —
/// what survives is in the same ingestion order it was stored in.
/// </para>
/// </remarks>
public sealed class ConversationContextPolicy
{
    /// <summary>
    /// The most messages replayed on one turn.
    /// </summary>
    /// <remarks>
    /// Forty is roughly twenty exchanges — long enough that a customer can refer
    /// back to what they said several minutes ago, short enough that a long
    /// support session does not quietly become an expensive one.
    /// </remarks>
    public int MaxMessages { get; init; } = 40;

    /// <summary>
    /// The most characters of conversation history replayed on one turn, across
    /// all messages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sixteen thousand characters is a large conversation by any ordinary measure
    /// and still a bounded request. It is counted over message text only: the
    /// agent's instructions are sent separately and are never traded off against
    /// history, because an agent that loses its instructions to make room for chat
    /// stops being the agent the workspace configured.
    /// </para>
    /// <para>
    /// <strong>Reduced from 24,000 when knowledge retrieval arrived, deliberately.</strong>
    /// Knowledge may contribute up to 8,000 characters of its own, and leaving
    /// this at 24,000 would have raised the size -- and therefore the cost -- of
    /// every grounded turn by half without anybody deciding to. The combined
    /// budget is what it always was: 16,000 of history plus 8,000 of knowledge is
    /// the same 24,000 this platform sent before any of this existed.
    /// </para>
    /// <para>
    /// Whole messages are dropped from the oldest end when the window is tight,
    /// never shortened, and the order they are sent in stays ingestion order.
    /// </para>
    /// </remarks>
    public int MaxTotalCharacters { get; init; } = 16_000;

    /// <summary>
    /// Selects the turns to send, from a conversation's history in ingestion order.
    /// </summary>
    /// <param name="history">
    /// The conversation's messages, oldest first, in the order this platform
    /// ingested them. Never sorted by <c>SentAt</c>: ingestion order is the
    /// platform's ordering and the only one that is stable.
    /// </param>
    /// <returns>Turns in ingestion order, oldest first. Possibly empty.</returns>
    public IReadOnlyList<AiTurn> SelectTurns(IReadOnlyList<ConversationMessageRecord> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        List<AiTurn> selected = [];
        int characters = 0;

        // Backwards from the newest, so what survives a tight bound is the recent
        // exchange rather than the opening of a conversation nobody remembers.
        for (int index = history.Count - 1; index >= 0; index--)
        {
            ConversationMessageRecord message = history[index];

            AiTurnRole? role = RoleOf(message.Sender);

            if (role is null)
            {
                continue;
            }

            // Defensive, and it drops rather than truncates. Nothing this surface
            // accepts can exceed a turn's own limit -- the public bound is far
            // below it -- but a message ingested by some future channel could, and
            // shortening somebody's words to make them fit is the one thing this
            // policy must never do.
            if (message.Text.Length > AiTurn.MaxTextLength)
            {
                continue;
            }

            if (selected.Count == MaxMessages)
            {
                break;
            }

            if (characters + message.Text.Length > MaxTotalCharacters && selected.Count > 0)
            {
                break;
            }

            selected.Add(role == AiTurnRole.User
                ? AiTurn.FromUser(message.Text)
                : AiTurn.FromAssistant(message.Text));

            characters += message.Text.Length;
        }

        // Back into ingestion order. The window was chosen from the end; the
        // conversation is still read from the beginning.
        selected.Reverse();

        return selected;
    }

    /// <summary>
    /// How a stored sender appears to the AI, or nothing if it does not appear.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>System notes are not replayed.</strong> They are the platform
    /// talking about the conversation, not anybody talking in it. Presenting them
    /// as a customer turn would let internal audit text be read as something the
    /// customer said — and would let anything the platform ever records about a
    /// conversation become an instruction to the model.
    /// </para>
    /// <para>
    /// <strong>Human agent messages are not replayed either, for now.</strong>
    /// Human handoff does not exist, so no conversation can contain one. When it
    /// does, whether a human's words are shown to the AI as its own prior turns —
    /// they are not the AI's — is a decision that belongs with the handoff design
    /// rather than guessed at here.
    /// </para>
    /// </remarks>
    private static AiTurnRole? RoleOf(MessageSender sender) => sender switch
    {
        MessageSender.Customer => AiTurnRole.User,
        MessageSender.AiAgent => AiTurnRole.Assistant,
        _ => null,
    };
}
