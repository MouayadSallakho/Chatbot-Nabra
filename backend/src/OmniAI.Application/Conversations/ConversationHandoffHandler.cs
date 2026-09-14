using OmniAI.Application.Authorization;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Authorization;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;

namespace OmniAI.Application.Conversations;

/// <summary>Take a conversation over from the AI, or hand it back.</summary>
/// <param name="ToHuman">
/// <see langword="true"/> to take over, <see langword="false"/> to return it.
/// One command rather than two because the authorisation, the locking, the
/// precondition and the announcement are identical and only the aggregate call
/// differs; two handlers would be the same forty lines twice.
/// </param>
public sealed record SetConversationHandlingCommand(
    Guid TenantId,
    Guid ConversationId,
    bool ToHuman,
    ConcurrencyToken Expected);

/// <summary>
/// Moves a conversation between being answered by the AI and by a person.
/// </summary>
/// <remarks>
/// <strong>It takes the same per-conversation lock generation takes, and that is
/// the point of the whole file.</strong> Without it, "take over" can land in the
/// middle of a turn: the provider call has already been paid for and is in
/// flight, the handler is about to append its reply, and this write says nobody
/// should be answering any more. The transcript then gets an AI reply after a
/// human took the conversation over, which is precisely the contradiction a
/// customer would notice and nobody could explain.
/// <para>
/// <strong>An in-flight provider call is never cancelled.</strong> It has been
/// bought already; abandoning it saves nothing and loses an answer that may be
/// the right one. So this waits, and the first operation to hold the lock owns
/// the transition — a turn that started before the takeover finishes and appends
/// its reply, and the takeover applies immediately afterwards. Deterministic,
/// and the order is whatever the database decided rather than whatever the
/// scheduler happened to do.
/// </para>
/// <para>
/// <strong>Returning to the AI answers nothing retroactively.</strong> That is a
/// property of the aggregate and of the runtime, not of this handler: generation
/// is only ever started by a new customer turn, so messages that arrived while a
/// person was handling the conversation stay unanswered by the AI for ever.
/// </para>
/// </remarks>
public sealed class ConversationHandoffHandler(
    TenantAuthorizer authorizer,
    IConversationInboxStore store,
    IConversationLock conversationLock,
    IConversationRealtimeNotifier realtime,
    TimeProvider clock)
{
    private readonly TenantAuthorizer _authorizer =
        authorizer ?? throw new ArgumentNullException(nameof(authorizer));

    private readonly IConversationInboxStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    private readonly IConversationLock _lock =
        conversationLock ?? throw new ArgumentNullException(nameof(conversationLock));

    private readonly IConversationRealtimeNotifier _realtime =
        realtime ?? throw new ArgumentNullException(nameof(realtime));

    private readonly TimeProvider _clock =
        clock ?? throw new ArgumentNullException(nameof(clock));

    /// <returns>
    /// The conversation as it now stands, or <see langword="null"/> when the
    /// caller is not entitled or no such conversation belongs to this workspace.
    /// </returns>
    /// <exception cref="PermissionDeniedException">
    /// A member of the workspace without <c>conversations.handoff</c>.
    /// </exception>
    /// <exception cref="ConversationVersionMismatchException">
    /// The conversation changed after the caller read it.
    /// </exception>
    public async Task<Versioned<Conversation>?> HandleAsync(
        SetConversationHandlingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        TenantId tenantId = TenantId.From(command.TenantId);
        ConversationId conversationId = ConversationId.From(command.ConversationId);

        // Reading the transcript is a prerequisite for changing who answers it:
        // a person who may hand a conversation over but may not see it is being
        // asked to make a decision blind.
        if (!await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsView, cancellationToken)
            .ConfigureAwait(false))
        {
            return null;
        }

        await _authorizer
            .RequireAsync(tenantId, PlatformPermissions.ConversationsHandoff, cancellationToken)
            .ConfigureAwait(false);

        // Nothing is read before the lock is held. Reading first and locking
        // second would mean deciding against a version that a turn already in
        // flight is about to replace.
        await using IAsyncDisposable hold = await _lock
            .AcquireAsync(conversationId.Value, cancellationToken)
            .ConfigureAwait(false);

        Versioned<Conversation>? found = await _store
            .FindForUpdateAsync(tenantId, conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (found is null)
        {
            return null;
        }

        Conversation conversation = found.Value;

        if (found.Version != command.Expected)
        {
            throw new ConversationVersionMismatchException(command.Expected);
        }

        DateTimeOffset now = _clock.GetUtcNow();

        bool changed = command.ToHuman
            ? conversation.TakeOverByHuman(now)
            : conversation.ReturnToAi(now);

        if (!changed)
        {
            // Somebody pressed the button the conversation was already on. Their
            // intent is satisfied, so this is a success — but saving would burn a
            // version and invalidate everybody else's screen for no change.
            return found;
        }

        ConcurrencyToken version = await _store
            .SaveAsync(conversation, command.Expected, cancellationToken)
            .ConfigureAwait(false);

        await _realtime
            .AnnounceQuietlyAsync(tenantId, conversationId, cancellationToken)
            .ConfigureAwait(false);

        return new Versioned<Conversation>(conversation, version);
    }
}
