using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.Users;

namespace OmniAI.Application.Tests.Conversations;

/// <summary>
/// An in-memory inbox store.
/// </summary>
/// <remarks>
/// <strong>The idempotency index is modelled, not skipped.</strong> A double
/// that let the same client message id through twice would make every test of
/// the reply path pass while the real thing double-posted; the dictionary here
/// plays the part of the unique index, and the store returns the stored reply
/// rather than writing a second one — exactly as PostgreSQL does.
/// </remarks>
internal sealed class FakeConversationInboxStore : IConversationInboxStore
{
    private readonly Dictionary<ConversationId, Conversation> _conversations = [];
    private readonly Dictionary<(ConversationId, ClientMessageId), ConversationMessageRecord> _replies = [];
    private readonly Dictionary<ConversationId, uint> _versions = [];
    private readonly HashSet<(TenantId, UserId)> _members = [];
    private readonly List<ConversationMessageAuthor> _eligible = [];
    private readonly Lock _sync = new();

    private long _sequence = 100;

    /// <summary>How many replies were genuinely written, duplicates excluded.</summary>
    public int Written { get; private set; }

    /// <summary>Gate held open across a reply, for exercising a real race.</summary>
    public TaskCompletionSource? PauseBeforeWrite { get; set; }

    public FakeConversationInboxStore Add(Conversation conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        lock (_sync)
        {
            _conversations[conversation.Id] = conversation;
            _versions[conversation.Id] = 1;
        }

        return this;
    }

    public FakeConversationInboxStore AddMember(
        TenantId tenantId,
        UserId member,
        string email,
        bool mayReply)
    {
        lock (_sync)
        {
            _members.Add((tenantId, member));

            if (mayReply)
            {
                _eligible.Add(new ConversationMessageAuthor(member.Value, email));
            }
        }

        return this;
    }

    /// <summary>The version a caller would read right now.</summary>
    public ConcurrencyToken VersionOf(ConversationId conversationId)
    {
        lock (_sync)
        {
            return Token(_versions[conversationId]);
        }
    }

    /// <summary>Simulates somebody else writing, so a held version goes stale.</summary>
    public void BumpVersion(ConversationId conversationId)
    {
        lock (_sync)
        {
            _versions[conversationId]++;
        }
    }

    public Task<Versioned<Conversation>?> FindForUpdateAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_conversations.TryGetValue(conversationId, out Conversation? conversation)
                || conversation.TenantId != tenantId)
            {
                return Task.FromResult<Versioned<Conversation>?>(null);
            }

            return Task.FromResult<Versioned<Conversation>?>(
                new Versioned<Conversation>(conversation, Token(_versions[conversationId])));
        }
    }

    public Task<ConcurrencyToken> SaveAsync(
        Conversation conversation,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        lock (_sync)
        {
            if (Token(_versions[conversation.Id]) != expected)
            {
                throw new ConversationVersionMismatchException(expected);
            }

            _versions[conversation.Id]++;

            return Task.FromResult(Token(_versions[conversation.Id]));
        }
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

        // Deliberately awaited before the key is claimed, so a test can hold two
        // callers here at once and prove the loser stores nothing.
        if (PauseBeforeWrite is not null)
        {
            await PauseBeforeWrite.Task.ConfigureAwait(false);
        }

        lock (_sync)
        {
            if (_replies.TryGetValue((conversation.Id, clientMessageId), out ConversationMessageRecord? stored))
            {
                return new HumanReplyOutcome(stored, WasAlreadyStored: true);
            }

            Message message = conversation.AddTextMessage(MessageSender.HumanAgent, text, sentAt);

            ConversationMessageRecord record = new(
                message.Id.Value,
                message.Sender,
                message.Text,
                message.SentAt,
                _sequence++,
                new ConversationMessageAuthor(sender.Value, "recorded@example.test"));

            _replies[(conversation.Id, clientMessageId)] = record;
            Written++;

            return new HumanReplyOutcome(record, WasAlreadyStored: false);
        }
    }

    public Task<bool> IsMemberAsync(
        TenantId tenantId,
        UserId member,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_members.Contains((tenantId, member)));
        }
    }

    public Task<IReadOnlyList<ConversationMessageAuthor>> ListEligibleAssigneesAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyList<ConversationMessageAuthor>>([.. _eligible]);
        }
    }

    private static ConcurrencyToken Token(uint version) =>
        ConcurrencyToken.From($"v{version}");
}

/// <summary>
/// A conversation lock that really serialises, and says who held it.
/// </summary>
/// <remarks>
/// A double that granted every request immediately would make the takeover race
/// test pass without proving anything. This one is a real mutual exclusion, so a
/// test can start a slow operation, attempt a second, and observe that the
/// second waited.
/// </remarks>
internal sealed class SerialisingConversationLock : IConversationLock, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Guid> _acquired = [];

    /// <summary>Every acquisition, in the order it was granted.</summary>
    public IReadOnlyList<Guid> Acquired
    {
        get
        {
            lock (_acquired)
            {
                return [.. _acquired];
            }
        }
    }

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (_acquired)
        {
            _acquired.Add(conversationId);
        }

        return new Hold(_gate);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class Hold(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();

            return ValueTask.CompletedTask;
        }
    }
}
