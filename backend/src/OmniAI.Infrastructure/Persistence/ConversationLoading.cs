using Microsoft.EntityFrameworkCore;
using OmniAI.Domain.Conversations;

namespace OmniAI.Infrastructure.Persistence;

/// <summary>
/// Loading a conversation together with its history in the order it was ingested.
/// </summary>
/// <remarks>
/// This is a query helper, not a repository: no interface, no injection, no
/// abstraction over the context. It exists because ordering a collection
/// navigation is not expressible through <c>Include</c> in EF Core, so the
/// history has to be loaded by an explicit ordered query whose results EF fixes
/// up into the aggregate. Any code that reads a conversation's messages must go
/// through here, or the order it gets back is whatever PostgreSQL happened to
/// return.
/// <para>
/// <strong>This is a read path, and appending a message does not use it.</strong>
/// A conversation may hold tens of thousands of messages, so the write path
/// loads only the conversation row -- <c>context.Conversations</c> with no
/// message query -- appends through the aggregate and saves. Ordering is
/// allocated from a PostgreSQL sequence and needs no knowledge of what is
/// already stored. Loading complete history and loading the state required to
/// append are two different things, and only reconstitution needs the first.
/// </para>
/// <para>
/// Application-facing persistence contracts arrive with the first real use case,
/// not here.
/// </para>
/// </remarks>
public static class ConversationLoading
{
    /// <summary>
    /// Loads one conversation and its messages in persisted ingestion order.
    /// </summary>
    /// <returns>The conversation, or <see langword="null"/> if no such conversation exists.</returns>
    public static async Task<Conversation?> FindConversationWithMessagesAsync(
        this OmniAiDbContext context,
        ConversationId id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        Conversation? conversation = await context.Conversations
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return null;
        }

        await context.Entry(conversation)
            .Collection(loaded => loaded.Messages)
            .Query()
            .OrderBy(message => EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        return conversation;
    }
}
