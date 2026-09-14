using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.WebChat.Persistence;
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
    IWebChatRuntimeStore store,
    WebChatPolicy policy)
{
    private readonly PublicWebChatAccess _access =
        access ?? throw new ArgumentNullException(nameof(access));

    private readonly IWebChatRuntimeStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

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

        ConversationHandling handling = await _store
            .ReadHandlingAsync(session.ConversationId, cancellationToken)
            .ConfigureAwait(false)
            ?? ConversationHandling.Ai;

        return new PublicChatMessagePage(
            [.. page.Items.Where(Visible).Select(ToView)],
            page.NextCursor,
            handling.ToString());
    }

    /// <summary>
    /// Whether a stored message belongs in a visitor's own view of the chat.
    /// </summary>
    /// <remarks>
    /// Platform audit notes are not part of the conversation as far as the person
    /// having it is concerned. A human agent's messages are: somebody at the
    /// workspace wrote them to this visitor, and withholding them would mean the
    /// customer could not read the reply they were waiting for.
    /// Filtering here rather than in the query keeps the cursor meaningful: pages
    /// are still cut on the stored ingestion sequence, so a filtered message does
    /// not shift the boundary between pages.
    /// </remarks>
    private static bool Visible(ConversationMessageRecord message) =>
        message.Sender is MessageSender.Customer
            or MessageSender.AiAgent
            or MessageSender.HumanAgent;

    /// <summary>
    /// Narrows a stored message to what its own visitor may see.
    /// </summary>
    /// <remarks>
    /// <strong><c>SentBy</c> is dropped here, and that is the only reason this
    /// method is worth a comment.</strong> The stored record names the employee
    /// who wrote a reply, because an administrator looking at their own team
    /// needs to know. A customer does not, and an email address is a real
    /// person's contact details: the sender a visitor is told about is the role
    /// that wrote it, never the individual. The view type has no field for one,
    /// so this cannot be undone by forgetting.
    /// </remarks>
    private static PublicChatMessageView ToView(ConversationMessageRecord message) =>
        new(message.Id, message.Sender.ToString(), message.Text, message.SentAt);
}
