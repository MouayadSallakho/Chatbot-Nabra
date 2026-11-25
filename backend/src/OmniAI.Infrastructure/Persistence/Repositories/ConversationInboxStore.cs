using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The writes an operational inbox makes, in PostgreSQL.
/// </summary>
/// <remarks>
/// Tracked reads, unlike <see cref="ConversationRepository"/>: everything here
/// loads a conversation in order to change it, and the change tracker is what
/// turns an aggregate method call into an update.
/// </remarks>
internal sealed class ConversationInboxStore(OmniAiDbContext context) : IConversationInboxStore
{
    private const string UniqueViolation = "23505";

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task<Versioned<Conversation>?> FindForUpdateAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        // The tenant is in the predicate, so a conversation id from another
        // workspace finds nothing rather than being found and then rejected.
        Conversation? conversation = await _context.Conversations
            .FirstOrDefaultAsync(
                candidate => candidate.Id == conversationId && candidate.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return null;
        }

        uint version = _context.Entry(conversation)
            .Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .CurrentValue;

        return new Versioned<Conversation>(conversation, RowVersionTokens.Encode(version));
    }

    public async Task<ConcurrencyToken> SaveAsync(
        Conversation conversation,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        // The token the caller presented is written back as the original value,
        // so PostgreSQL compares against the revision they actually read rather
        // than the one this request happened to load.
        if (RowVersionTokens.TryDecode(expected, out uint original))
        {
            _context.Entry(conversation)
                .Property<uint>(PersistenceSchema.XminConcurrencyToken)
                .OriginalValue = original;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConversationVersionMismatchException(expected);
        }

        uint version = _context.Entry(conversation)
            .Property<uint>(PersistenceSchema.XminConcurrencyToken)
            .CurrentValue;

        return RowVersionTokens.Encode(version);
    }

    public Task<ConcurrencyToken> SaveHandlingChangeAsync(
        Conversation conversation,
        ConversationHandlingEvent change,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        // Added before the save, so the conversation's new handling and the row
        // that records it are one transaction: a refused precondition stores
        // neither, and a stored change always has its history.
        _context.ConversationHandlingEvents.Add(change);

        return SaveAsync(conversation, expected, cancellationToken);
    }

    public async Task<long> ReadNewestSequenceAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        await _context.Set<Message>()
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .MaxAsync(
                message => (long?)EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence),
                cancellationToken)
            .ConfigureAwait(false)
        ?? 0;

    public async Task<bool> MarkReadAsync(
        TenantId tenantId,
        ConversationId conversationId,
        UserId member,
        long sequence,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        // One statement, so the rules cannot interleave with another writer:
        //  - the conversation must be this workspace's (no row is inserted for a
        //    foreign or unknown id, and the caller answers not-found);
        //  - the sequence is clamped to the newest message, so a marker never
        //    claims a message that does not exist yet;
        //  - an existing marker only moves forward (GREATEST), and read_at moves
        //    only with it.
        // The membership key (user, tenant) is enforced by the foreign key.
        int written = await _context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO conversation_reads (conversation_id, user_id, tenant_id, last_read_sequence, read_at)
                SELECT c.id, {member.Value}, c.tenant_id,
                       LEAST({sequence}, COALESCE((SELECT max(m.ingestion_sequence) FROM messages m WHERE m.conversation_id = c.id), 0)),
                       {readAt}
                FROM conversations c
                WHERE c.id = {conversationId.Value} AND c.tenant_id = {tenantId.Value}
                ON CONFLICT (conversation_id, user_id) DO UPDATE
                SET last_read_sequence = GREATEST(conversation_reads.last_read_sequence, EXCLUDED.last_read_sequence),
                    read_at = CASE
                        WHEN EXCLUDED.last_read_sequence > conversation_reads.last_read_sequence THEN EXCLUDED.read_at
                        ELSE conversation_reads.read_at
                    END
                """,
                cancellationToken)
            .ConfigureAwait(false);

        return written > 0;
    }

    public async Task<int> MarkAllReadAsync(
        TenantId tenantId,
        UserId member,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        // One statement (Phase 39.1):
        //  - only this workspace's conversations, and only those with a message
        //    (a conversation without one is never unread);
        //  - each marker goes to the conversation's newest message, read through
        //    ix_messages_conversation_ingestion backwards;
        //  - an existing marker moves only forward, and a conversation already
        //    read to its newest message is not touched at all (the DO UPDATE's
        //    WHERE), so the row count is exactly the conversations that were unread.
        // The membership key (user, tenant) is enforced by the foreign key.
        return await _context.Database
            .ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO conversation_reads (conversation_id, user_id, tenant_id, last_read_sequence, read_at)
                SELECT c.id, {member.Value}, c.tenant_id, newest.sequence, {readAt}
                FROM conversations c
                CROSS JOIN LATERAL (
                    SELECT m.ingestion_sequence AS sequence
                    FROM messages m
                    WHERE m.conversation_id = c.id
                    ORDER BY m.ingestion_sequence DESC
                    LIMIT 1) newest
                WHERE c.tenant_id = {tenantId.Value}
                ON CONFLICT (conversation_id, user_id) DO UPDATE
                SET last_read_sequence = EXCLUDED.last_read_sequence,
                    read_at = EXCLUDED.read_at
                WHERE EXCLUDED.last_read_sequence > conversation_reads.last_read_sequence
                """,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountUnreadAsync(
        TenantId tenantId,
        UserId member,
        CancellationToken cancellationToken = default)
    {
        // The list's IsUnread rule in SQL: a newest message, and no marker or a
        // marker behind it. One statement, the same index as above.
        return await _context.Database
            .SqlQuery<int>(
                $"""
                SELECT count(*)::int AS "Value"
                FROM conversations c
                CROSS JOIN LATERAL (
                    SELECT m.ingestion_sequence AS sequence
                    FROM messages m
                    WHERE m.conversation_id = c.id
                    ORDER BY m.ingestion_sequence DESC
                    LIMIT 1) newest
                LEFT JOIN conversation_reads r
                    ON r.conversation_id = c.id AND r.user_id = {member.Value}
                WHERE c.tenant_id = {tenantId.Value}
                  AND (r.last_read_sequence IS NULL OR newest.sequence > r.last_read_sequence)
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<HumanReplyOutcome> AppendReplyAsync(
        Conversation conversation,
        UserId sender,
        ClientMessageId clientMessageId,
        string text,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        if (conversation.Channel == ChannelKind.Instagram && await _context.Set<InstagramSimulatorThread>()
            .AnyAsync(thread => thread.ConversationId == conversation.Id && thread.ExternalOwner, cancellationToken).ConfigureAwait(false))
        {
            throw new InstagramExternalOwnerException();
        }

        // The aggregate validates the text and allocates the message; the reply
        // row records who wrote it. One save, so a stored message always has an
        // author and a stored author always has a message.
        Message message = conversation.AddTextMessage(MessageSender.HumanAgent, text, sentAt);

        HumanReply reply = HumanReply.Record(
            conversation.Id,
            conversation.TenantId,
            sender,
            clientMessageId,
            message.Id,
            sentAt);

        var author = await ReadAuthorAsync(sender, cancellationToken).ConfigureAwait(false);

        _context.HumanReplies.Add(reply);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsDuplicateReply(failure))
        {
            // Two requests carrying the same client message id reached this line
            // at once, and the index settled it. Nothing of this attempt is
            // stored: the message and the reply row were one transaction, so
            // losing the row loses the message with it.
            _context.ChangeTracker.Clear();

            ConversationMessageRecord? stored = await ReadStoredReplyAsync(
                    conversation.Id,
                    clientMessageId,
                    cancellationToken)
                .ConfigureAwait(false);

            // The winning transaction committed before this one failed, so its
            // row is readable. If it somehow is not, saying so is better than
            // inventing a message that may not be what the customer will see.
            return stored is null
                ? throw new InvalidOperationException(
                    "A duplicate human reply was refused but the original could not be read back.")
                : new HumanReplyOutcome(stored, WasAlreadyStored: true);
        }

        long sequence = _context.Entry(message)
            .Property<long>(PersistenceSchema.MessageIngestionSequence)
            .CurrentValue;

        return new HumanReplyOutcome(
            new ConversationMessageRecord(message.Id.Value, message.Sender, message.Text, message.SentAt, sequence, author),
            WasAlreadyStored: false);
    }

    public Task<bool> IsMemberAsync(
        TenantId tenantId,
        UserId member,
        CancellationToken cancellationToken = default) =>
        _context.TenantMemberships.AnyAsync(
            membership => membership.UserId == member && membership.TenantId == tenantId,
            cancellationToken);

    public async Task<IReadOnlyList<ConversationMessageAuthor>> ListEligibleAssigneesAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        // Roles are loaded and the permission is computed in memory rather than
        // filtered in SQL, because what a role permits is a Domain rule: an Owner
        // stores no permission rows at all and answers with the whole catalogue.
        // A WHERE clause over role_permissions would therefore silently exclude
        // every Owner -- the one member guaranteed to be able to reply.
        // Two queries rather than a join, because a join key that reaches inside
        // a value object -- membership.UserId.Value -- is not translatable: the
        // value converter maps the whole object to a column and EF has nothing
        // to map a member of it to. The failure is a 500 the first time an
        // administrator opens the assignment control, not a compile error.
        List<Guid> userIds = await _context.TenantMemberships
            .AsNoTracking()
            .Where(membership => membership.TenantId == tenantId)
            .Select(membership => membership.UserId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userIds.Count == 0)
        {
            return [];
        }

        // Users are Identity's own table, keyed on a plain Guid, so this one is
        // an ordinary translatable Contains.
        var members = await _context.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email, user.DisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Value objects in the predicate, for the reason given above.
        List<UserId> membersOfThisWorkspace = [.. userIds.Select(UserId.From)];

        var assignments = await _context.TenantRoleAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.TenantId == tenantId
                && membersOfThisWorkspace.Contains(assignment.UserId))
            .Join(
                _context.TenantRoles.AsNoTracking().Include(role => role.Grants),
                assignment => assignment.TenantRoleId,
                role => role.Id,
                (assignment, role) => new { assignment.UserId, Role = role })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<Guid, List<TenantRole>> rolesByUser = assignments
            .GroupBy(entry => entry.UserId.Value)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Role).ToList());

        List<ConversationMessageAuthor> eligible = [];

        foreach (var member in members.OrderBy(member => member.Email))
        {
            if (!rolesByUser.TryGetValue(member.Id, out List<TenantRole>? roles))
            {
                continue;
            }

            if (EffectivePermissions.Union(roles).Contains(PlatformPermissions.ConversationsReply))
            {
                eligible.Add(new ConversationMessageAuthor(member.Id, member.Email!, member.DisplayName));
            }
        }

        return eligible;
    }

    private async Task<ConversationMessageRecord?> ReadStoredReplyAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        CancellationToken cancellationToken)
    {
        var stored = await _context.HumanReplies.AsNoTracking()
            .Where(reply => reply.ConversationId == conversationId && reply.ClientMessageId == clientMessageId)
            .Join(_context.Set<Message>(), reply => reply.MessageId, message => message.Id,
                (reply, message) => new {
                    reply.Sender,
                    Message = new ConversationMessageRecord(message.Id.Value, message.Sender, message.Text,
                        message.SentAt, EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence), null)
                })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return stored is null ? null : stored.Message with
        {
            SentBy = await ReadAuthorAsync(stored.Sender, cancellationToken).ConfigureAwait(false)
        };
    }

    private Task<ConversationMessageAuthor> ReadAuthorAsync(UserId sender, CancellationToken cancellationToken) =>
        _context.Users.Where(user => user.Id == sender.Value)
            .Select(user => new ConversationMessageAuthor(user.Id, user.Email!, user.DisplayName))
            .SingleAsync(cancellationToken);

    /// <summary>
    /// Whether a failed save was the idempotency index refusing a duplicate.
    /// </summary>
    /// <remarks>
    /// Matched on the constraint name, not merely on "some unique violation". A
    /// different broken constraint is a different problem and must not be
    /// swallowed as though the same reply had simply been sent twice.
    /// </remarks>
    private static bool IsDuplicateReply(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && postgres.ConstraintName == "ux_human_replies_client_message";
}
