using Microsoft.EntityFrameworkCore;
using Npgsql;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.WebChat;

namespace OmniAI.Infrastructure.Persistence.Repositories;

/// <summary>
/// The write path behind a live website chat conversation, in PostgreSQL.
/// </summary>
/// <remarks>
/// <strong>Each method is one short transaction, and none of them spans the
/// provider call.</strong> The customer's message is committed before the
/// generator is reached and the reply is committed after it returns, so the
/// database is never waiting on somebody else's service while holding locks.
/// </remarks>
internal sealed class WebChatRuntimeStore(OmniAiDbContext context) : IWebChatRuntimeStore
{
    /// <summary>PostgreSQL's SQLSTATE for a unique violation.</summary>
    private const string UniqueViolation = "23505";

    private readonly OmniAiDbContext _context =
        context ?? throw new ArgumentNullException(nameof(context));

    public async Task StartConversationAsync(
        Conversation conversation,
        Customer customer,
        WebChatSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(session);

        // One SaveChanges, therefore one transaction. A conversation without its
        // session would be unreachable by the visitor who just started it, and a
        // conversation without its customer would not be insertable at all -- the
        // foreign key is what makes "every conversation belongs to somebody" a
        // fact about the database rather than a habit of the code.
        _context.Customers.Add(customer);
        _context.Conversations.Add(conversation);
        _context.WebChatSessions.Add(session);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<WebChatSession?> FindSessionAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        _context.WebChatSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.ConversationId == conversationId, cancellationToken);

    public Task<ConversationTurn?> FindTurnAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        CancellationToken cancellationToken = default) =>
        _context.ConversationTurns
            .FirstOrDefaultAsync(
                turn => turn.ConversationId == conversationId
                    && turn.ClientMessageId == clientMessageId,
                cancellationToken);

    public async Task<ConversationTurn?> BeginTurnAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        string text,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        // Only the conversation row, not its history. Appending needs the
        // aggregate's state and nothing else -- the ingestion sequence is
        // allocated from PostgreSQL and needs no knowledge of what is stored.
        Conversation? conversation = await _context.Conversations
            .FirstOrDefaultAsync(candidate => candidate.Id == conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (conversation is null)
        {
            return null;
        }

        Message message = conversation.AddTextMessage(MessageSender.Customer, text, sentAt);

        ConversationTurn turn = ConversationTurn.Begin(
            conversationId,
            clientMessageId,
            message.Id,
            sentAt);

        _context.ConversationTurns.Add(turn);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (IsDuplicateTurn(failure))
        {
            // Two requests carrying the same client message id reached this line
            // at once and the index settled it. Nothing of this attempt is
            // stored -- the message and the turn were one transaction, so losing
            // the turn loses the message with it, which is the correct outcome
            // for a duplicate.
            //
            // The context is discarded by the caller's scope; the tracked
            // entities are detached here so nothing half-written is ever saved by
            // a later operation on the same request.
            _context.ChangeTracker.Clear();

            return null;
        }

        return turn;
    }

    public async Task<ConversationMessageRef> CompleteTurnAsync(
        ConversationTurn turn,
        string replyText,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        Conversation conversation = await RequireConversationAsync(turn, cancellationToken)
            .ConfigureAwait(false);

        Message reply = conversation.AddTextMessage(MessageSender.AiAgent, replyText, sentAt);

        turn.Complete(reply.Id, sentAt);

        _context.ConversationTurns.Update(turn);

        // The reply and the completion in one transaction. A reply stored without
        // its turn being completed would be answered again by the next duplicate,
        // and a completion without its reply would return a turn pointing at
        // nothing.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ConversationMessageRef(
            reply.Id.Value,
            reply.Text,
            reply.SentAt,
            _context.Entry(reply).Property<long>(PersistenceSchema.MessageIngestionSequence).CurrentValue);
    }

    public async Task FailTurnAsync(
        ConversationTurn turn,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        turn.Fail(failedAt);

        _context.ConversationTurns.Update(turn);

        // Only the turn. The customer's message was committed before the provider
        // was called and is deliberately left exactly as it is.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationMessageRef?> FindReplyAsync(
        ConversationTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        if (turn.ReplyMessageId is not MessageId replyId)
        {
            return null;
        }

        return await FindMessageAsync(replyId, cancellationToken).ConfigureAwait(false);
    }

    public Task<ConversationMessageRef?> FindCustomerMessageAsync(
        ConversationTurn turn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        return FindMessageAsync(turn.CustomerMessageId, cancellationToken);
    }

    private async Task<ConversationMessageRef?> FindMessageAsync(
        MessageId messageId,
        CancellationToken cancellationToken)
    {
        return await _context.Set<Message>()
            .AsNoTracking()
            .Where(message => message.Id == messageId)
            .Select(message => new ConversationMessageRef(
                message.Id.Value,
                message.Text,
                message.SentAt,
                EF.Property<long>(message, PersistenceSchema.MessageIngestionSequence)))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Conversation> RequireConversationAsync(
        ConversationTurn turn,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await _context.Conversations
            .FirstOrDefaultAsync(candidate => candidate.Id == turn.ConversationId, cancellationToken)
            .ConfigureAwait(false);

        // The turn was created against this conversation moments ago, so its
        // absence would mean the row has been deleted underneath a live request.
        // Raised rather than ignored: silently dropping a reply that has already
        // been paid for is the one outcome worse than an error.
        return conversation
            ?? throw new InvalidOperationException(
                "The conversation this turn belongs to no longer exists.");
    }

    public async Task<ConversationHandling?> ReadHandlingAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        // Untracked and projected to the one column. The caller is about to load
        // the same conversation for writing through BeginTurnAsync, and tracking
        // one row from two queries is how a change tracker ends up with an
        // opinion nobody intended.
        ConversationHandling[] found = await _context.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.Id == conversationId)
            .Select(conversation => conversation.Handling)
            .Take(1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return found.Length == 0 ? null : found[0];
    }

    public async Task SettleWithoutReplyAsync(
        ConversationTurn turn,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        turn.CompleteWithoutReply(settledAt);

        _context.ConversationTurns.Update(turn);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a failed save was the turn uniqueness index refusing a duplicate.
    /// </summary>
    /// <remarks>
    /// Matched on the constraint name, not merely on "some unique violation". A
    /// different broken constraint is a different problem and must not be
    /// swallowed as though a duplicate message had simply been sent twice.
    /// </remarks>
    private static bool IsDuplicateTurn(DbUpdateException failure) =>
        failure.InnerException is PostgresException postgres
        && postgres.SqlState == UniqueViolation
        && postgres.ConstraintName == "ux_conversation_turns_client_message";
}
