using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Conversations;

namespace OmniAI.Application.WhatsApp;

/// <summary>
/// Whether WhatsApp still allows a free-form reply to this customer.
/// </summary>
/// <remarks>
/// <strong>Meta's rule, as it stands.</strong> A customer messaging the business
/// opens a 24-hour customer service window, and each new customer message resets
/// it. Inside the window a business may send free-form text; outside it, only a
/// pre-approved template — and templates are out of scope for this phase, so
/// outside the window there is nothing this platform may send at all.
/// <para>
/// <strong>So the platform refuses rather than trying.</strong> Sending anyway
/// would produce a provider rejection, which costs a round trip and tells a
/// colleague something in Meta's words rather than its own. Refusing locally
/// gives an honest state immediately and never pretends a message was delivered.
/// </para>
/// <para>
/// <strong>Checked twice: when queueing, and again in the worker immediately
/// before the send.</strong> A delivery can sit in the outbox while the window
/// closes underneath it — a colleague typing a long reply, a worker catching up
/// after an outage — and the only check that matters is the one taken at the
/// moment of sending.
/// </para>
/// <para>
/// The window is measured from the last <em>customer</em> message in the
/// conversation, which is what Meta's rule is about. Replies from the business
/// do not extend it.
/// </para>
/// </remarks>
public sealed class CustomerServiceWindow(TimeProvider clock)
{
    /// <summary>
    /// How long a customer message keeps the window open.
    /// </summary>
    /// <remarks>
    /// Twenty-four hours, the current documented rule. A constant rather than a
    /// setting: it is Meta's policy, not this deployment's, and a workspace that
    /// could shorten or lengthen it would only be able to get it wrong.
    /// </remarks>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(24);

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>Whether a free-form reply is allowed right now.</summary>
    /// <param name="lastCustomerMessageAt">
    /// When the customer last wrote, or <see langword="null"/> if they never
    /// have.
    /// </param>
    public bool IsOpen(DateTimeOffset? lastCustomerMessageAt) =>
        lastCustomerMessageAt is DateTimeOffset last
        && _clock.GetUtcNow() - last < Duration;

    /// <summary>
    /// Whether a free-form reply to this conversation is allowed right now.
    /// </summary>
    /// <remarks>
    /// Reads the tail of the transcript rather than a stored timestamp. A cached
    /// "window closes at" column would be one more thing to keep correct across
    /// every path that appends a customer message, and it would be wrong exactly
    /// when a path forgot.
    /// </remarks>
    public async Task<bool> IsOpenAsync(
        IConversationRepository conversations,
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversations);

        IReadOnlyList<ConversationMessageRecord> tail = await conversations
            .ListRecentMessagesAsync(conversationId, limit: 50, cancellationToken)
            .ConfigureAwait(false);

        DateTimeOffset? lastCustomerMessage = tail
            .Where(message => message.Sender == MessageSender.Customer)
            .Select(message => (DateTimeOffset?)message.SentAt)
            .LastOrDefault();

        return IsOpen(lastCustomerMessage);
    }

    /// <summary>What a colleague is told when the window has closed.</summary>
    /// <remarks>
    /// Explains the rule and what it means for them, without blaming anybody and
    /// without claiming the message was sent.
    /// </remarks>
    public const string ClosedReason =
        "WhatsApp only allows a free-form reply within 24 hours of the customer's "
        + "last message, and that window has closed. The reply is saved here but "
        + "was not delivered.";
}
