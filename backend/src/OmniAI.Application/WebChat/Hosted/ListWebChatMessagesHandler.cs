using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Hosted;

/// <summary>A visitor asking for their own transcript.</summary>
/// <param name="ConversationId">The conversation the visitor named.</param>
/// <param name="AccessToken">The raw secret from the visitor's cookie.</param>
/// <param name="AfterSequence">Where to continue from, or the beginning.</param>
/// <param name="Limit">How many messages to return.</param>
/// <param name="Mode">
/// Which transport this read arrived through. Set by the endpoint that received
/// the request, never by the caller, so a credential presented at the wrong door
/// is refused before anything is read.
/// </param>
public sealed record ListWebChatMessagesQuery(
    Guid ConversationId,
    string? AccessToken,
    long? AfterSequence,
    int? Limit,
    WebChatClientMode Mode = WebChatClientMode.Hosted);

/// <summary>
/// A visitor's own transcript, and only their own.
/// </summary>
/// <remarks>
/// <strong>Readable even when the chat has been withdrawn.</strong> Disabling an
/// endpoint stops new conversations and new turns; it does not take away what a
/// visitor already said and was told. Erasing somebody's history the moment a
/// business changes its mind about a channel would be a retention decision made
/// by accident, and this is a service decision. A visitor whose chat is disabled
/// mid-conversation can still read back what happened — they simply cannot
/// continue it.
/// </remarks>
public sealed class ListWebChatMessagesHandler(
    PublicWebChatAccess access,
    IConversationRepository conversations,
    WebChatPolicy policy)
{
    private readonly PublicWebChatAccess _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly IConversationRepository _conversations =
        conversations ?? throw new ArgumentNullException(nameof(conversations));

    private readonly WebChatPolicy _policy =
        policy ?? throw new ArgumentNullException(nameof(policy));

    /// <returns>
    /// The page, or <see langword="null"/> when the credential does not open this
    /// conversation — which is also the answer for one that does not exist.
    /// </returns>
    public async Task<PublicChatMessagePage?> HandleAsync(
        ListWebChatMessagesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        ConversationId conversationId = ConversationId.From(query.ConversationId);

        WebChatSession? session = await _access
            .AuthoriseAsync(conversationId, query.AccessToken, query.Mode, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return null;
        }

        int limit = Math.Clamp(
            query.Limit ?? _policy.DefaultMessagePageSize,
            1,
            _policy.MaxMessagePageSize);

        ConversationMessagePage page = await _conversations
            .ListMessagesAsync(session.ConversationId, query.AfterSequence, limit, cancellationToken)
            .ConfigureAwait(false);

        return new PublicChatMessagePage(
            [.. page.Items.Where(Visible).Select(ToView)],
            page.NextCursor);
    }

    /// <summary>
    /// Whether a stored message belongs in a visitor's own view of the chat.
    /// </summary>
    /// <remarks>
    /// Platform audit notes are not part of the conversation as far as the person
    /// having it is concerned, and a human agent's messages cannot exist yet.
    /// Filtering here rather than in the query keeps the cursor meaningful: pages
    /// are still cut on the stored ingestion sequence, so a filtered message does
    /// not shift the boundary between pages.
    /// </remarks>
    private static bool Visible(ConversationMessageRecord message) =>
        message.Sender is MessageSender.Customer or MessageSender.AiAgent;

    private static PublicChatMessageView ToView(ConversationMessageRecord message) =>
        new(message.Id, message.Sender.ToString(), message.Text, message.SentAt);
}
