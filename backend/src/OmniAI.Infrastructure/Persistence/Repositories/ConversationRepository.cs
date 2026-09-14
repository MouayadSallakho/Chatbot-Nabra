using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

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
                conversation.Handling,
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
                    .FirstOrDefault(),

                // The id only. Turning it into a name is a second query, for the
                // reason set out on NameAssigneesAsync.
                AssignedTo: null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ConversationPage(
            await NameAssigneesAsync(items, cancellationToken).ConfigureAwait(false),
            page,
            pageSize,
            total);
    }

    public async Task<Versioned<ConversationDetail>?> FindAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        // The tenant is in the predicate. A conversation id belonging to another
        // workspace finds nothing rather than being found and then rejected.
        //
        // The version is projected alongside the detail rather than read from a
        // tracked entry, because this query is deliberately untracked: a list
        // screen reading a hundred conversations should not populate a change
        // tracker it will never save.
        var row = await _context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.Id == conversationId && conversation.TenantId == tenantId)
            .Select(conversation => new
            {
                Detail = new ConversationDetail(
                    conversation.Id.Value,
                    conversation.Channel,
                    conversation.Status,
                    conversation.Handling,
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
                    AssignedTo: null),
                Assigned = conversation.AssignedTo,
                Version = EF.Property<uint>(
                    conversation,
                    PersistenceSchema.XminConcurrencyToken),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        ConversationMessageAuthor? assignee = row.Assigned is null
            ? null
            : await FindMemberAsync(row.Assigned.Value, cancellationToken).ConfigureAwait(false);

        return new Versioned<ConversationDetail>(
            row.Detail with { AssignedTo = assignee },
            RowVersionTokens.Encode(row.Version));
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
            await NameAuthorsAsync(rows, cancellationToken).ConfigureAwait(false),
            hasMore && rows.Count > 0 ? rows[^1].IngestionSequence : null);
    }

    /// <summary>
    /// Names the colleague each conversation on this page is routed to.
    /// </summary>
    /// <remarks>
    /// <strong>A second query, for the same reason the transcript authors need
    /// one.</strong> A subquery from the conversation into the users table on a
    /// nullable value-object id is not translatable: EF Core cannot turn
    /// <c>AssignedTo.Value.Value</c> inside a projection into SQL, and the
    /// failure is a 500 on the first administrator who opens the inbox rather
    /// than a compile error.
    /// <para>
    /// A page where nothing is assigned — which is most pages, most of the time
    /// — makes no second query at all.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ConversationSummary>> NameAssigneesAsync(
        List<ConversationSummary> items,
        CancellationToken cancellationToken)
    {
        // Value objects throughout, never their unwrapped Guids. A predicate
        // that reaches inside one -- Contains(conversation.Id.Value) -- is not
        // translatable, because the value converter maps the whole object to a
        // column and EF has nothing to map a member of it to. Comparing the
        // objects themselves is what every other query here already does.
        List<ConversationId> onThisPage = [.. items.Select(item => ConversationId.From(item.Id))];

        var assignments = await _context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                onThisPage.Contains(conversation.Id) && conversation.AssignedTo != null)
            .Select(conversation => new
            {
                Conversation = conversation.Id.Value,
                Member = conversation.AssignedTo!.Value.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assignments.Count == 0)
        {
            return items;
        }

        // Users are Identity's table, keyed on a plain Guid, so this one is an
        // ordinary translatable Contains.
        List<Guid> members = [.. assignments.Select(assignment => assignment.Member).Distinct()];

        Dictionary<Guid, ConversationMessageAuthor> byMember = await _context.Users
            .AsNoTracking()
            .Where(user => members.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => new ConversationMessageAuthor(user.Id, user.Email!),
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, ConversationMessageAuthor> byConversation = assignments
            .Where(assignment => byMember.ContainsKey(assignment.Member))
            .ToDictionary(
                assignment => assignment.Conversation,
                assignment => byMember[assignment.Member]);

        return [.. items.Select(item =>
            byConversation.TryGetValue(item.Id, out ConversationMessageAuthor? author)
                ? item with { AssignedTo = author }
                : item)];
    }

    /// <summary>One member, named.</summary>
    private async Task<ConversationMessageAuthor?> FindMemberAsync(
        UserId member,
        CancellationToken cancellationToken) =>
        await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == member.Value)
            .Select(user => new ConversationMessageAuthor(user.Id, user.Email!))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Names the colleague behind each reply a person wrote.
    /// </summary>
    /// <remarks>
    /// <strong>A second query rather than a join, and that is a deliberate
    /// retreat.</strong> The obvious version — a correlated subquery in the
    /// projection, joining the reply record to the user — is not translatable:
    /// EF Core cannot turn a <c>Join</c> inside a <c>Select</c> into SQL, and
    /// the failure is a runtime exception on the first admin who opens a
    /// transcript, not a compile error. Expressing it as a left join in the main
    /// query is translatable but drags a nullable navigation through a
    /// projection that is otherwise a clean row-to-record mapping.
    /// <para>
    /// So the page is read as it always was, and the authors of the human
    /// replies on it are fetched in one further round trip keyed on the message
    /// ids already in hand. A page with no human replies — which is every page
    /// of every conversation the AI is handling — makes no second query at all.
    /// </para>
    /// <para>
    /// The email is published to the workspace's own screens and nowhere else.
    /// Every public transport builds its own view type, and none of them has a
    /// field this could be assigned to.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<ConversationMessageRecord>> NameAuthorsAsync(
        List<ConversationMessageRecord> rows,
        CancellationToken cancellationToken)
    {
        List<MessageId> written = [..
            rows.Where(row => row.Sender == MessageSender.HumanAgent)
                .Select(row => MessageId.From(row.Id))];

        if (written.Count == 0)
        {
            return rows;
        }

        // Value objects in the predicate, for the reason given on
        // NameAssigneesAsync.
        var replies = await _context.HumanReplies
            .AsNoTracking()
            .Where(reply => written.Contains(reply.MessageId))
            .Select(reply => new
            {
                Message = reply.MessageId.Value,
                Member = reply.Sender.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (replies.Count == 0)
        {
            return rows;
        }

        List<Guid> members = [.. replies.Select(reply => reply.Member).Distinct()];

        Dictionary<Guid, ConversationMessageAuthor> byMember = await _context.Users
            .AsNoTracking()
            .Where(user => members.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => new ConversationMessageAuthor(user.Id, user.Email!),
                cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, ConversationMessageAuthor> byMessage = replies
            .Where(reply => byMember.ContainsKey(reply.Member))
            .ToDictionary(reply => reply.Message, reply => byMember[reply.Member]);

        return [.. rows.Select(row =>
            byMessage.TryGetValue(row.Id, out ConversationMessageAuthor? author)
                ? row with { SentBy = author }
                : row)];
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
