using Microsoft.EntityFrameworkCore;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.Customers;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;
using OmniAI.Domain.WhatsApp;

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

    /// <summary>
    /// The reason code a failed AI turn is published with.
    /// </summary>
    /// <remarks>
    /// One code, because a turn stores only that it failed, not why: the provider
    /// error is deliberately never persisted (it can carry provider detail), and
    /// the approved plan adds no column for a cause. "No answer was sent" is the
    /// stable fact a colleague needs.
    /// </remarks>
    internal const string FailedTurnReason = "generation_failed";

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<ConversationPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        ConversationListFilter filter,
        UserId viewer,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Conversation> owned = _context.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.TenantId == tenantId);

        if (filter == ConversationListFilter.NeedsPerson)
        {
            owned = NeedingAPerson(owned);
        }

        int total = await owned.CountAsync(cancellationToken).ConfigureAwait(false);

        // Most recently active first, with the id as a tie-break so two
        // conversations sharing an instant never swap places between pages and
        // hide a row or show one twice.
        var rows = await owned
            .OrderByDescending(conversation => conversation.LastActivityAt)
            .ThenByDescending(conversation => conversation.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(conversation => new
            {
                Summary = new ConversationSummary(
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
                    AssignedTo: null),
                conversation.CustomerId,

                // Unread for this reader: two more correlated subqueries in the
                // same statement, never a query per row.
                Newest = conversation.Messages.Max(message =>
                    (long?)EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)),
                Read = _context.ConversationReads
                    .Where(read => read.ConversationId == conversation.Id && read.UserId == viewer)
                    .Select(read => (long?)read.LastReadSequence)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<Guid, ConversationCustomer> customers = await DescribeCustomersAsync(
                tenantId,
                [.. rows.Select(row => (row.Summary.Id, row.CustomerId, row.Summary.Channel))],
                cancellationToken)
            .ConfigureAwait(false);

        List<ConversationSummary> items =
        [
            .. rows.Select(row => row.Summary with
            {
                Customer = customers.GetValueOrDefault(row.Summary.Id),
                Unread = IsUnread(row.Newest, row.Read),
            }),
        ];

        return new ConversationPage(
            await NameAssigneesAsync(items, cancellationToken).ConfigureAwait(false),
            page,
            pageSize,
            total);
    }

    /// <summary>
    /// The conversations waiting on a person: open, the customer wrote last, and
    /// one of: a person is handling it; the AI's turn for that last message
    /// failed; or the AI is handling it but that turn completed without a reply.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third branch widens the plan's D-5 at the owner's request (Phase 37.4,
    /// Revision 1). A turn completes without a reply only while a person handles
    /// the conversation; handed back to the AI before anybody answered, that
    /// message would otherwise wait for ever, because the AI never answers
    /// retroactively. While a person still handles it the first branch already
    /// counts it, so the turn test needs no handling condition of its own.
    /// </para>
    /// One predicate over the newest message, reached by the existing
    /// <c>ix_messages_conversation_ingestion</c> index read backwards, and its turn
    /// by <c>ux_conversation_turns_customer_message</c>. No index is added to
    /// either table (an analytics test freezes their index sets); the handling
    /// branch has <c>ix_conversations_tenant_handling_activity</c> (M6).
    /// </remarks>
    private IQueryable<Conversation> NeedingAPerson(IQueryable<Conversation> owned) =>
        owned.Where(conversation =>
            conversation.Status == ConversationStatus.Open
            && conversation.Messages
                .OrderByDescending(message =>
                    EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
                .Take(1)
                .Any(last =>
                    last.Sender == MessageSender.Customer
                    && (conversation.Handling == ConversationHandling.Human
                        || _context.ConversationTurns.Any(turn =>
                            turn.CustomerMessageId == last.Id
                            && (turn.Status == ConversationTurnStatus.Failed
                                || (turn.Status == ConversationTurnStatus.Completed
                                    && turn.ReplyMessageId == null))))));

    /// <summary>
    /// Unread when the newest message is beyond the reader's marker, or the reader
    /// has no marker and there is a message at all.
    /// </summary>
    private static bool IsUnread(long? newest, long? read) =>
        newest is long last && (read is not long marker || last > marker);

    public async Task<Versioned<ConversationDetail>?> FindAsync(
        TenantId tenantId,
        ConversationId conversationId,
        UserId viewer,
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
                conversation.CustomerId,
                Newest = conversation.Messages.Max(message =>
                    (long?)EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)),
                Read = _context.ConversationReads
                    .Where(read => read.ConversationId == conversation.Id && read.UserId == viewer)
                    .Select(read => (long?)read.LastReadSequence)
                    .FirstOrDefault(),
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

        IReadOnlyDictionary<Guid, ConversationCustomer> customer = await DescribeCustomersAsync(
                tenantId,
                [(row.Detail.Id, row.CustomerId, row.Detail.Channel)],
                cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<ConversationDetail>(
            row.Detail with
            {
                AssignedTo = assignee,
                Customer = customer.GetValueOrDefault(row.Detail.Id),
                Unread = IsUnread(row.Newest, row.Read),
                LastReadSequence = row.Read,
            },
            RowVersionTokens.Encode(row.Version));
    }

    /// <summary>
    /// Who each of these conversations is with: name, label code, masked number
    /// and short code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A fixed number of queries per page, never one per row.</strong>
    /// The customers' names, the channels each has used (the label rule needs
    /// them), and — only when the page has a WhatsApp conversation — the number
    /// each WhatsApp conversation runs on.
    /// </para>
    /// <para>
    /// <strong>The number is masked here, on the value object, straight after the
    /// read.</strong> The raw <c>wa_id</c> never leaves this method and there is no
    /// field that could carry it. A conversation always names its canonical
    /// customer: a merge moves conversations in the same transaction.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, ConversationCustomer>> DescribeCustomersAsync(
        TenantId tenantId,
        IReadOnlyList<(Guid Conversation, CustomerId Customer, ChannelKind Channel)> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return new Dictionary<Guid, ConversationCustomer>();
        }

        List<CustomerId> customerIds = [.. rows.Select(row => row.Customer).Distinct()];

        var names = await _context.Customers
            .AsNoTracking()
            .Where(customer => customer.TenantId == tenantId && customerIds.Contains(customer.Id))
            .Select(customer => new { customer.Id, customer.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<CustomerId, CustomerDisplayName?> nameOf = names.ToDictionary(
            entry => entry.Id,
            entry => entry.DisplayName);

        var pairs = await _context.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.TenantId == tenantId && customerIds.Contains(conversation.CustomerId))
            .Select(conversation => new { conversation.CustomerId, conversation.Channel })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<CustomerId, IReadOnlyList<ChannelKind>> channelsOf = pairs
            .GroupBy(pair => pair.CustomerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ChannelKind>)[.. group.Select(pair => pair.Channel).Order()]);

        Dictionary<Guid, string> referenceOf = [];

        List<ConversationId> onWhatsApp =
        [
            .. rows.Where(row => row.Channel == ChannelKind.WhatsApp)
                .Select(row => ConversationId.From(row.Conversation)),
        ];

        if (onWhatsApp.Count > 0)
        {
            // Masking happens on the value object, after the read, as the customer
            // directory does: projecting the raw column into a string here would
            // create a variable holding an unmasked number.
            var contacts = await _context.WhatsAppContacts
                .AsNoTracking()
                .Where(contact =>
                    contact.TenantId == tenantId && onWhatsApp.Contains(contact.ConversationId))
                .OrderByDescending(contact => contact.StartedAt)
                .Select(contact => new { contact.ConversationId, Number = contact.CustomerId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var contact in contacts)
            {
                referenceOf.TryAdd(contact.ConversationId.Value, contact.Number.Masked);
            }
        }

        Dictionary<Guid, ConversationCustomer> described = [];

        foreach ((Guid conversation, CustomerId customer, _) in rows)
        {
            CustomerDisplayName? displayName = nameOf.GetValueOrDefault(customer);
            IReadOnlyList<ChannelKind> channels = channelsOf.GetValueOrDefault(customer) ?? [];

            described[conversation] = new ConversationCustomer(
                customer.Value,
                displayName?.Value,
                CustomerLabels.CodeFor(displayName, channels),
                referenceOf.GetValueOrDefault(conversation),
                CustomerShortCode.For(customer));
        }

        return described;
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

    public async Task<ConversationMessagePage> ListMessagesBeforeAsync(
        ConversationId conversationId,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Message> query = _context.Set<Message>()
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId);

        if (beforeSequence is long cursor)
        {
            query = query.Where(message =>
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence) < cursor);
        }

        // Taken from the end (the index read backwards), one more than asked for
        // so whether anything is earlier is answered by the same query, then put
        // back into ingestion order: pages are ascending in every direction.
        List<ConversationMessageRecord> rows = await query
            .OrderByDescending(message =>
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

        bool hasEarlier = rows.Count > limit;

        if (hasEarlier)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        rows.Reverse();

        // The newest page has nothing later by construction. A page read before a
        // cursor usually has (the cursor's own message), but a cursor beyond the
        // newest message does not, so it is asked rather than assumed.
        bool hasLater = beforeSequence is not null
            && rows.Count > 0
            && await HasMessagesAfterAsync(conversationId, rows[^1].IngestionSequence, cancellationToken)
                .ConfigureAwait(false);

        return new ConversationMessagePage(
            await NameAuthorsAsync(rows, cancellationToken).ConfigureAwait(false),
            hasLater ? rows[^1].IngestionSequence : null,
            hasEarlier && rows.Count > 0 ? rows[0].IngestionSequence : null);
    }

    public Task<bool> HasMessagesBeforeAsync(
        ConversationId conversationId,
        long sequence,
        CancellationToken cancellationToken = default) =>
        _context.Set<Message>().AnyAsync(
            message => message.ConversationId == conversationId
                && EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence) < sequence,
            cancellationToken);

    private Task<bool> HasMessagesAfterAsync(
        ConversationId conversationId,
        long sequence,
        CancellationToken cancellationToken) =>
        _context.Set<Message>().AnyAsync(
            message => message.ConversationId == conversationId
                && EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence) > sequence,
            cancellationToken);

    public async Task<IReadOnlyList<TranscriptEvent>> ListTranscriptEventsAsync(
        ConversationId conversationId,
        long fromSequence,
        long toSequence,
        CancellationToken cancellationToken = default)
    {
        // Takeovers and returns, by ix_conversation_handling_events_conversation.
        var changes = await _context.ConversationHandlingEvents
            .AsNoTracking()
            .Where(change =>
                change.ConversationId == conversationId
                && change.AfterSequence >= fromSequence
                && change.AfterSequence <= toSequence)
            .Select(change => new
            {
                Id = change.Id.Value,
                change.Kind,
                change.OccurredAt,
                change.AfterSequence,
                Actor = change.Actor.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Failed AI turns, placed after the customer message each one failed to
        // answer. A turn completed without a reply (a person was handling the
        // conversation) is not a failure and is never read here.
        var failures = await _context.ConversationTurns
            .AsNoTracking()
            .Where(turn =>
                turn.ConversationId == conversationId
                && turn.Status == ConversationTurnStatus.Failed)
            .Join(
                _context.Set<Message>().AsNoTracking(),
                turn => turn.CustomerMessageId,
                message => message.Id,
                (turn, message) => new
                {
                    Id = turn.Id.Value,
                    turn.UpdatedAt,
                    Sequence = EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence),
                })
            .Where(failure => failure.Sequence >= fromSequence && failure.Sequence <= toSequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, ConversationMessageAuthor> actors = [];

        if (changes.Count > 0)
        {
            List<Guid> members = [.. changes.Select(change => change.Actor).Distinct()];

            actors = await _context.Users
                .AsNoTracking()
                .Where(user => members.Contains(user.Id))
                .ToDictionaryAsync(
                    user => user.Id,
                    user => new ConversationMessageAuthor(user.Id, user.Email!),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return
        [
            .. changes
                .Select(change => new TranscriptEvent(
                    change.Id,
                    change.Kind == ConversationHandlingEventKind.TakenOver
                        ? TranscriptEventKind.TakenOver
                        : TranscriptEventKind.ReturnedToAi,
                    change.OccurredAt,
                    change.AfterSequence,
                    actors.GetValueOrDefault(change.Actor),
                    Reason: null))
                .Concat(failures.Select(failure => new TranscriptEvent(
                    failure.Id,
                    TranscriptEventKind.AiTurnFailed,
                    failure.UpdatedAt,
                    failure.Sequence,
                    Actor: null,
                    FailedTurnReason)))
                .OrderBy(entry => entry.AfterSequence)
                .ThenBy(entry => entry.At)
                .ThenBy(entry => entry.Id),
        ];
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
