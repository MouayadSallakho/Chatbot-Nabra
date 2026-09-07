using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reading conversations, in PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>Every ordering and every page boundary is the ingestion sequence.</strong>
/// Nothing here sorts history by <c>SentAt</c>. A conversation's order is the
/// order this platform received its messages, and that is a stored, monotonic,
/// gap-tolerant number rather than a timestamp a channel chose — which is exactly
/// why it can also be a cursor.
/// </remarks>
internal sealed class ConversationRepository(OmniAiDbContext context) : IConversationRepository
{
    /// <summary>How much of a message is shown as a list preview.</summary>
    private const int PreviewLength = 160;

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ConversationPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Conversation> owned = _context.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.TenantId == tenantId);

        int total = await owned.CountAsync(cancellationToken).ConfigureAwait(false);

        // Most recently active first, with the id as a tie-break so two
        // conversations sharing an instant never swap places between pages and
        // hide a row or show one twice.
        List<ConversationSummary> items = await owned
            .OrderByDescending(conversation => conversation.LastActivityAt)
            .ThenByDescending(conversation => conversation.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(conversation => new ConversationSummary(
                conversation.Id.Value,
                conversation.Channel,
                conversation.Status,
                conversation.StartedAt,
                conversation.LastActivityAt,
                _context.WebChatSessions
                    .Where(session => session.ConversationId == conversation.Id)
                    .Join(
                        _context.AiAgents,
                        session => session.AiAgentId,
                        agent => agent.Id,
                        (session, agent) => agent.Name.Value)
                    .FirstOrDefault(),
                conversation.Messages.Count,

                // The newest message by ingestion order, cut to a preview length.
                // Read as a correlated subquery so a list of fifty conversations
                // is one round trip rather than fifty-one, and so no transcript is
                // loaded to show one line of it.
                conversation.Messages
                    .OrderByDescending(message =>
                        EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
                    .Select(message => message.Text.Length > PreviewLength
                        ? message.Text.Substring(0, PreviewLength)
                        : message.Text)
                    .FirstOrDefault(),
                conversation.Messages
                    .OrderByDescending(message =>
                        EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
                    .Select(message => (MessageSender?)message.Sender)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ConversationPage(items, page, pageSize, total);
    }

    public async Task<ConversationDetail?> FindAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        // The tenant is in the predicate. A conversation id belonging to another
        // workspace finds nothing rather than being found and then rejected.
        return await _context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.Id == conversationId && conversation.TenantId == tenantId)
            .Select(conversation => new ConversationDetail(
                conversation.Id.Value,
                conversation.Channel,
                conversation.Status,
                conversation.StartedAt,
                conversation.LastActivityAt,
                _context.WebChatSessions
                    .Where(session => session.ConversationId == conversation.Id)
                    .Join(
                        _context.AiAgents,
                        session => session.AiAgentId,
                        agent => agent.Id,
                        (session, agent) => agent.Name.Value)
                    .FirstOrDefault(),
                conversation.Messages.Count))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> BelongsToAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        _context.Conversations.AnyAsync(
            conversation => conversation.Id == conversationId && conversation.TenantId == tenantId,
            cancellationToken);

    public async Task<ConversationMessagePage> ListMessagesAsync(
        ConversationId conversationId,
        long? afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Message> query = _context.Set<Message>()
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId);

        if (afterSequence is long cursor)
        {
            query = query.Where(message =>
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence) > cursor);
        }

        // One more than asked for, so whether another page exists is answered by
        // the same query rather than by a second count that could disagree with it.
        List<ConversationMessageRecord> rows = await query
            .OrderBy(message =>
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
            .Take(limit + 1)
            .Select(message => new ConversationMessageRecord(
                message.Id.Value,
                message.Sender,
                message.Text,
                message.SentAt,
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = rows.Count > limit;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new ConversationMessagePage(
            rows,
            hasMore && rows.Count > 0 ? rows[^1].IngestionSequence : null);
    }

    public async Task<IReadOnlyList<ConversationMessageRecord>> ListRecentMessagesAsync(
        ConversationId conversationId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Taken from the end in the database, then turned back into ingestion
        // order here. Ordering descending is how the tail is selected without
        // reading the whole history; the caller is still handed oldest-first, so
        // the context policy never has to reorder anything.
        List<ConversationMessageRecord> newestFirst = await _context.Set<Message>()
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .OrderByDescending(message =>
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
            .Take(limit)
            .Select(message => new ConversationMessageRecord(
                message.Id.Value,
                message.Sender,
                message.Text,
                message.SentAt,
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        newestFirst.Reverse();

        return newestFirst;
    }

    public async Task<AiAgentId?> FindBoundAgentAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        return await _context.WebChatSessions
            .AsNoTracking()
            .Where(session => session.ConversationId == conversationId)
            .Select(session => (AiAgentId?)session.AiAgentId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
