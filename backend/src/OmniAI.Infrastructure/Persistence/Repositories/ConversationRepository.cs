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

    private sealed class InboxAgent
    {
        public ConversationId ConversationId { get; init; }
        public required string Name { get; init; }
    }

    private IQueryable<InboxAgent> InboxAgentNames(TenantId tenant)
    {
        var bindings = _context.WebChatSessions.Where(s => s.TenantId == tenant)
            .Select(s => new { s.ConversationId, AgentId = s.AiAgentId })
            .Concat(_context.Set<MessengerSimulatorThread>().Where(s => s.TenantId == tenant)
                .Select(s => new { s.ConversationId, s.AgentId }))
            .Concat(_context.Set<InstagramSimulatorThread>().Where(s => s.TenantId == tenant)
                .Select(s => new { s.ConversationId, s.AgentId }));
        return from binding in bindings
               join agent in _context.AiAgents.Where(a => a.TenantId == tenant) on binding.AgentId equals agent.Id
               select new InboxAgent { ConversationId = binding.ConversationId, Name = agent.Name.Value };
    }

    public async Task<IReadOnlyList<ConversationMessageAuthor>> ListAssignedMembersAsync(
        TenantId tenantId, CancellationToken cancellationToken)
    {
        var assigned = await _context.Conversations.Where(c => c.TenantId == tenantId && c.AssignedTo != null)
            .Select(c => c.AssignedTo).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        var members = await NameMembersAsync(assigned, cancellationToken).ConfigureAwait(false);
        return [.. members.Values.OrderBy(member => member.DisplayName ?? member.Email, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.UserId)];
    }

    public async Task<ConversationPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        ConversationListFilter filter,
        UserId viewer,
        string? search = null,
        InboxFilters? criteria = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<HandledConversation> owned = WithHandlingFacts(
            _context.Conversations
                .AsNoTracking()
                .Where(conversation => conversation.TenantId == tenantId));

        if (filter == ConversationListFilter.NeedsPerson)
        {
            // The very expression each row carries as its reason (Phase 38.7a),
            // so the filter and the flag cannot disagree.
            owned = owned.Where(handled => handled.Reason != NoReason);
        }
        else if (filter == ConversationListFilter.Mine)
        {
            // Phase 39.1 (BA39-5): assigned to the reader, or taken over by the
            // reader, by the same takeover expression the row's takenOverBy
            // publishes, so the tab and the chip cannot disagree.
            UserId? me = viewer;

            owned = owned.Where(handled =>
                handled.Conversation.AssignedTo == me || handled.TakenOverBy == me);
        }

        if (criteria?.Channel is { } channel)
        {
            owned = owned.Where(row => row.Conversation.Channel == channel);
        }

        if (criteria?.Status is { } status)
        {
            owned = owned.Where(row => row.Conversation.Status == status);
        }

        if (criteria?.AssignedTo is { } assignee)
        {
            UserId member = UserId.From(assignee);
            owned = owned.Where(row => row.Conversation.AssignedTo == member);
        }
        else if (criteria?.Unassigned == true)
        {
            owned = owned.Where(row => row.Conversation.AssignedTo == null);
        }

        if (criteria?.ActiveFrom?.ToUniversalTime() is { } start)
        {
            owned = owned.Where(row => row.Conversation.LastActivityAt >= start);
        }

        if (criteria?.ActiveBefore?.ToUniversalTime() is { } end)
        {
            owned = owned.Where(row => row.Conversation.LastActivityAt < end);
        }

        if (!string.IsNullOrEmpty(search))
        {
            string pattern = "%" + search.Replace("!", "!!", StringComparison.Ordinal)
                .Replace("%", "!%", StringComparison.Ordinal)
                .Replace("_", "!_", StringComparison.Ordinal) + "%";
            string[] suffixes = CustomerShortCode.MatchingIdSuffixes(search);
            var namedCustomers = _context.Customers.FromSql($"""
                SELECT * FROM customers
                WHERE tenant_id = {tenantId.Value} AND (display_name ILIKE {pattern} ESCAPE '!'
                    OR ({suffixes.Length > 0} AND right(id::text, 8) = ANY ({suffixes})))
                """);

            // Compare only the reference exposed by the inbox, including for very short numbers.
            string reference = search.Length == 4 && search.All(char.IsAsciiDigit) ? "••••" + search : search;
            bool searchReference = reference.StartsWith('•') && reference.Length <= 8;
            var byReference = _context.Conversations.FromSql($"""
                SELECT current.conversation_id AS id FROM (
                    SELECT DISTINCT ON (conversation_id) conversation_id,
                        CASE WHEN length(customer_id) <= 4 THEN repeat('•', length(customer_id))
                             ELSE '••••' || right(customer_id, 4) END AS masked
                    FROM whatsapp_contacts WHERE tenant_id = {tenantId.Value}
                    ORDER BY conversation_id, started_at DESC, id DESC
                ) AS current WHERE masked = {reference}
                """);
            owned = owned.Where(handled =>
                namedCustomers.Any(customer => customer.Id == handled.Conversation.CustomerId) ||
                (searchReference && byReference.Any(match => match.Id == handled.Conversation.Id)) ||
                handled.Conversation.Messages.Any(message => EF.Functions.ILike(message.Text, pattern, "!")));
        }

        int total = await owned.CountAsync(cancellationToken).ConfigureAwait(false);

        // Most recently active first, with the id as a tie-break so two
        // conversations sharing an instant never swap places between pages and
        // hide a row or show one twice.
        var agentsForInbox = InboxAgentNames(tenantId);
        var rows = await owned
            .OrderByDescending(handled => handled.Conversation.LastActivityAt)
            .ThenByDescending(handled => handled.Conversation.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(handled => new
            {
                Summary = new ConversationSummary(
                    handled.Conversation.Id.Value,
                    handled.Conversation.Channel,
                    handled.Conversation.Status,
                    handled.Conversation.Handling,
                    handled.Conversation.StartedAt,
                    handled.Conversation.LastActivityAt,
                    agentsForInbox.Where(binding => binding.ConversationId == handled.Conversation.Id)
                        .Select(binding => binding.Name).FirstOrDefault(),
                    handled.Conversation.Messages.Count,

                    // The newest message by ingestion order, cut to a preview length.
                    // Read as a correlated subquery so a list of fifty conversations
                    // is one round trip rather than fifty-one, and so no transcript is
                    // loaded to show one line of it.
                    handled.Conversation.Messages
                        .OrderByDescending(message =>
                            EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
                        .Select(message => message.Text.Length > PreviewLength
                            ? message.Text.Substring(0, PreviewLength)
                            : message.Text)
                        .FirstOrDefault(),
                    handled.Conversation.Messages
                        .OrderByDescending(message =>
                            EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
                        .Select(message => (MessageSender?)message.Sender)
                        .FirstOrDefault(),

                    // Ids only, below. Turning them into names is one more query
                    // for the page, for the reason set out on NameMembersAsync.
                    AssignedTo: null),
                handled.Conversation.CustomerId,
                Assigned = handled.Conversation.AssignedTo,
                handled.TakenOverBy,
                handled.Reason,

                // Unread for this reader: two more correlated subqueries in the
                // same statement, never a query per row.
                Newest = handled.Conversation.Messages.Max(message =>
                    (long?)EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)),
                Read = _context.ConversationReads
                    .Where(read => read.ConversationId == handled.Conversation.Id && read.UserId == viewer)
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

        IReadOnlyDictionary<Guid, ConversationMessageAuthor> members = await NameMembersAsync(
                [.. rows.SelectMany(row => new[] { row.Assigned, row.TakenOverBy })],
                cancellationToken)
            .ConfigureAwait(false);

        List<ConversationSummary> items =
        [
            .. rows.Select(row => row.Summary with
            {
                AssignedTo = Named(members, row.Assigned),
                Customer = customers.GetValueOrDefault(row.Summary.Id),
                Unread = IsUnread(row.Newest, row.Read),
                TakenOverBy = Named(members, row.TakenOverBy),
                NeedsPersonReason = ReasonFor(row.Reason),
            }),
        ];

        return new ConversationPage(items, page, pageSize, total);
    }

    /// <summary>
    /// A conversation, with who took it over and why it waits on a person
    /// computed beside it in the same statement (Phase 38.7a).
    /// </summary>
    /// <remarks>
    /// A nominal type rather than an anonymous one, so the list, its count, its
    /// filter and the detail all compose over the one projection.
    /// </remarks>
    private sealed class HandledConversation
    {
        public Conversation Conversation { get; init; } = null!;

        /// <summary>See <see cref="WithHandlingFacts"/>.</summary>
        public UserId? TakenOverBy { get; init; }

        /// <summary>A <see cref="NeedsPersonReason"/> value, or <see cref="NoReason"/>.</summary>
        public int Reason { get; init; }
    }

    /// <summary>The reason of a conversation that waits on nobody.</summary>
    private const int NoReason = 0;

    private const int PersonHandlingReason = (int)NeedsPersonReason.PersonHandling;

    private const int AiTurnFailedReason = (int)NeedsPersonReason.AiTurnFailed;

    private const int ReturnedUnansweredReason = (int)NeedsPersonReason.ReturnedUnanswered;

    /// <summary>
    /// Who took each conversation over, and why it waits on a person (Phase 38.7a).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The reason is the one definition behind both
    /// <c>filter=needs-person</c> and the <c>needsPerson</c> flag.</strong> Open,
    /// the customer wrote last, and one of: a person is handling it
    /// (<c>PersonHandling</c>); the AI's turn for that last message failed
    /// (<c>AiTurnFailed</c>); or that turn completed without a reply
    /// (<c>ReturnedUnanswered</c>). The third branch widens the plan's D-5 at the
    /// owner's request (Phase 37.4, Revision 1): a turn completes without a reply
    /// only while a person handles the conversation, and handed back to the AI
    /// before anybody answered, that message would otherwise wait for ever,
    /// because the AI never answers retroactively.
    /// </para>
    /// <para>
    /// The branches are tested in that order, so <c>PersonHandling</c> wins when a
    /// person handles a conversation whose last turn also failed or went
    /// unanswered: their reply is what is awaited now. The two turn branches are
    /// exclusive, because a turn is failed or completed, never both.
    /// </para>
    /// <para>
    /// One scalar subquery over the newest message, reached by the existing
    /// <c>ix_messages_conversation_ingestion</c> index read backwards, and its turn
    /// by <c>ux_conversation_turns_customer_message</c>. No index is added to
    /// either table (an analytics test freezes their index sets).
    /// </para>
    /// <para>
    /// <strong>Who took it over</strong> is the actor of the newest handling event
    /// (by transcript position, then time, then id), read through
    /// <c>ix_conversation_handling_events_conversation</c>, when that event is a
    /// takeover and a person handles the conversation now. A return is newer than
    /// the takeover it ends, so it answers null, as does a takeover from before
    /// Phase 37.4, which recorded no event. The workspace is in the predicate as
    /// well as the conversation.
    /// </para>
    /// </remarks>
    private IQueryable<HandledConversation> WithHandlingFacts(IQueryable<Conversation> owned) =>
        owned.Select(conversation => new HandledConversation
        {
            Conversation = conversation,
            TakenOverBy = conversation.Handling == ConversationHandling.Human
                ? _context.ConversationHandlingEvents
                    .Where(change =>
                        change.ConversationId == conversation.Id
                        && change.TenantId == conversation.TenantId)
                    .OrderByDescending(change => change.AfterSequence)
                    .ThenByDescending(change => change.OccurredAt)
                    .ThenByDescending(change => change.Id)
                    .Select(change => change.Kind == ConversationHandlingEventKind.TakenOver
                        ? (UserId?)change.Actor
                        : null)
                    .FirstOrDefault()
                : null,
            Reason = conversation.Status != ConversationStatus.Open
                ? NoReason
                : conversation.Messages
                    .OrderByDescending(message =>
                        EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
                    .Select(last => last.Sender != MessageSender.Customer
                        ? NoReason
                        : conversation.Handling == ConversationHandling.Human
                            ? PersonHandlingReason
                            : _context.ConversationTurns.Any(turn =>
                                turn.CustomerMessageId == last.Id
                                && turn.Status == ConversationTurnStatus.Failed)
                                ? AiTurnFailedReason
                                : _context.ConversationTurns.Any(turn =>
                                    turn.CustomerMessageId == last.Id
                                    && turn.Status == ConversationTurnStatus.Completed
                                    && turn.ReplyMessageId == null)
                                    ? ReturnedUnansweredReason
                                    : NoReason)
                    .FirstOrDefault(),
        });

    private static NeedsPersonReason? ReasonFor(int reason) =>
        reason == NoReason ? null : (NeedsPersonReason)reason;

    private static ConversationMessageAuthor? Named(
        IReadOnlyDictionary<Guid, ConversationMessageAuthor> members,
        UserId? member) =>
        member is UserId id ? members.GetValueOrDefault(id.Value) : null;

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
        var agentsForInbox = InboxAgentNames(tenantId);
        var row = await WithHandlingFacts(
                _context.Conversations
                    .AsNoTracking()
                    .Where(conversation =>
                        conversation.Id == conversationId && conversation.TenantId == tenantId))
            .Select(handled => new
            {
                Detail = new ConversationDetail(
                    handled.Conversation.Id.Value,
                    handled.Conversation.Channel,
                    handled.Conversation.Status,
                    handled.Conversation.Handling,
                    handled.Conversation.StartedAt,
                    handled.Conversation.LastActivityAt,
                    agentsForInbox.Where(binding => binding.ConversationId == handled.Conversation.Id)
                        .Select(binding => binding.Name).FirstOrDefault(),
                    handled.Conversation.Messages.Count,
                    AssignedTo: null),
                Assigned = handled.Conversation.AssignedTo,
                handled.TakenOverBy,
                handled.Reason,
                Version = EF.Property<uint>(
                    handled.Conversation,
                    PersistenceSchema.XminConcurrencyToken),
                handled.Conversation.CustomerId,
                Newest = handled.Conversation.Messages.Max(message =>
                    (long?)EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)),
                Read = _context.ConversationReads
                    .Where(read => read.ConversationId == handled.Conversation.Id && read.UserId == viewer)
                    .Select(read => (long?)read.LastReadSequence)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        IReadOnlyDictionary<Guid, ConversationMessageAuthor> members = await NameMembersAsync(
                [row.Assigned, row.TakenOverBy],
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyDictionary<Guid, ConversationCustomer> customer = await DescribeCustomersAsync(
                tenantId,
                [(row.Detail.Id, row.CustomerId, row.Detail.Channel)],
                cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<ConversationDetail>(
            row.Detail with
            {
                AssignedTo = Named(members, row.Assigned),
                Customer = customer.GetValueOrDefault(row.Detail.Id),
                Unread = IsUnread(row.Newest, row.Read),
                LastReadSequence = row.Read,
                TakenOverBy = Named(members, row.TakenOverBy),
                NeedsPersonReason = ReasonFor(row.Reason),
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
                .ThenByDescending(contact => contact.Id)
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
                    user => new ConversationMessageAuthor(user.Id, user.Email!, user.DisplayName),
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
    /// Names the colleagues a page of conversations is routed to or taken over by,
    /// in one query (Phase 38.7a: the takeover joined the assignee).
    /// </summary>
    /// <remarks>
    /// <strong>A second query, for the same reason the transcript authors need
    /// one.</strong> A subquery from the conversation into the users table on a
    /// nullable value-object id is not translatable: EF Core cannot turn
    /// <c>AssignedTo.Value.Value</c> inside a projection into SQL, and the
    /// failure is a 500 on the first administrator who opens the inbox rather
    /// than a compile error. The ids themselves come with the page's own
    /// statement.
    /// <para>
    /// Named exactly as the transcript names its event actors: the account's
    /// email, whether or not they are still a member. A page where nobody is
    /// assigned or has taken over makes no second query at all.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, ConversationMessageAuthor>> NameMembersAsync(
        IReadOnlyList<UserId?> named,
        CancellationToken cancellationToken)
    {
        // Users are Identity's table, keyed on a plain Guid, so this one is an
        // ordinary translatable Contains.
        List<Guid> members =
        [
            .. named.Where(member => member is not null).Select(member => member!.Value.Value).Distinct(),
        ];

        if (members.Count == 0)
        {
            return new Dictionary<Guid, ConversationMessageAuthor>();
        }

        return await _context.Users
            .AsNoTracking()
            .Where(user => members.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => new ConversationMessageAuthor(user.Id, user.Email!, user.DisplayName),
                cancellationToken)
            .ConfigureAwait(false);
    }

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
                user => new ConversationMessageAuthor(user.Id, user.Email!, user.DisplayName),
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

    public Task<DateTimeOffset?> LastCustomerMessageAtAsync(
        ConversationId conversationId, CancellationToken cancellationToken = default) =>
        _context.Set<Message>().Where(message =>
                message.ConversationId == conversationId && message.Sender == MessageSender.Customer)
            .OrderByDescending(message => EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence))
            .Select(message => (DateTimeOffset?)message.SentAt)
            .FirstOrDefaultAsync(cancellationToken);

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
