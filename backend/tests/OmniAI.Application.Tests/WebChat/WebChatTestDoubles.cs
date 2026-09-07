using System.Security.Cryptography;
using System.Text;
using OmniAI.Application.Ai;
using OmniAI.Application.Concurrency;
using OmniAI.Application.Conversations;
using OmniAI.Application.Conversations.Persistence;
using OmniAI.Application.WebChat;
using OmniAI.Application.WebChat.Persistence;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.Tests.WebChat;

/// <summary>
/// An AI generator the test controls completely.
/// </summary>
/// <remarks>
/// <strong>It counts its calls, and that count is the point.</strong> Every call
/// to a real provider costs the workspace money, so "was the provider invoked
/// exactly once" is a correctness property of this runtime rather than an
/// implementation detail — and the only way to assert it is to be the provider.
/// </remarks>
internal sealed class RecordingGenerator : IAiTextGenerator
{
    private readonly Func<AiTextGenerationRequest, Task<AiTextGenerationResult>> _behaviour;
    private int _calls;

    private RecordingGenerator(Func<AiTextGenerationRequest, Task<AiTextGenerationResult>> behaviour) =>
        _behaviour = behaviour;

    /// <summary>How many times the provider was actually reached.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Every request the provider was given, in the order it got them.</summary>
    public List<AiTextGenerationRequest> Requests { get; } = [];

    /// <summary>Answers with a fixed reply.</summary>
    public static RecordingGenerator Replying(string reply = "The answer.") =>
        new(_ => Task.FromResult(AiTextGenerationResult.Create(reply)));

    /// <summary>Answers by echoing something about the request it was given.</summary>
    public static RecordingGenerator Answering(Func<AiTextGenerationRequest, string> reply) =>
        new(request => Task.FromResult(AiTextGenerationResult.Create(reply(request))));

    /// <summary>Fails every call with the supplied fault.</summary>
    public static RecordingGenerator Failing(Func<Exception> fault) =>
        new(_ => Task.FromException<AiTextGenerationResult>(fault()));

    /// <summary>Blocks until released, so a test can hold a turn open.</summary>
    public static RecordingGenerator Gated(
        TaskCompletionSource gate,
        string reply = "The answer.") =>
        new(async _ =>
        {
            await gate.Task.ConfigureAwait(false);

            return AiTextGenerationResult.Create(reply);
        });

    public async Task<AiTextGenerationResult> GenerateAsync(
        AiTextGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);

        lock (Requests)
        {
            Requests.Add(request);
        }

        return await _behaviour(request).ConfigureAwait(false);
    }
}

/// <summary>
/// A conversation lock that really excludes, in one process.
/// </summary>
/// <remarks>
/// <strong>This double is not the production design and must not be mistaken for
/// it.</strong> Production uses a PostgreSQL advisory lock precisely because an
/// in-process gate stops working the moment there are two API instances. Here
/// there is one process by construction, so a semaphore per conversation models
/// the contract exactly — and lets a test prove that the handler takes the lock,
/// holds it across the provider call, and releases it.
/// </remarks>
internal sealed class FakeConversationLock : IConversationLock
{
    private readonly Dictionary<Guid, SemaphoreSlim> _gates = [];

    /// <summary>The most conversations held at once. Must never exceed one each.</summary>
    public int MaxConcurrentHolders { get; private set; }

    private int _holders;

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate;

        lock (_gates)
        {
            if (!_gates.TryGetValue(conversationId, out SemaphoreSlim? existing))
            {
                existing = new SemaphoreSlim(1, 1);
                _gates[conversationId] = existing;
            }

            gate = existing;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (_gates)
        {
            _holders++;
            MaxConcurrentHolders = Math.Max(MaxConcurrentHolders, _holders);
        }

        return new Release(this, gate);
    }

    private sealed class Release(FakeConversationLock owner, SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (owner._gates)
            {
                owner._holders--;
            }

            gate.Release();

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Real token minting, so the tests exercise the shapes the platform issues.
/// </summary>
/// <remarks>
/// Not a stub returning "token": the access check hinges on a digest comparison,
/// and a double that skipped the hashing would prove nothing about whether the
/// right value is compared.
/// </remarks>
internal sealed class TestWebChatTokens : IWebChatAccessTokens
{
    public NewWebChatAccessToken Create()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            .PadRight(43, 'a')[..43];

        return new NewWebChatAccessToken(token, Digest(token));
    }

    public WebChatAccessTokenHash? Recognise(string? token) =>
        string.IsNullOrEmpty(token) || token.Length != 43 ? null : Digest(token);

    private static WebChatAccessTokenHash Digest(string token) =>
        WebChatAccessTokenHash.From(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}

/// <summary>Public keys, minted the way the platform mints them.</summary>
internal sealed class TestWebChatPublicKeys : IWebChatPublicKeys
{
    public WebChatPublicKey Create() =>
        WebChatPublicKey.Create(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(11))[..WebChatPublicKey.Length]);
}

/// <summary>
/// An in-memory world: endpoints, conversations, sessions, turns and messages.
/// </summary>
/// <remarks>
/// <strong>It keeps ingestion order the way PostgreSQL does.</strong> Messages
/// get a monotonic sequence from a single counter and are only ever read back in
/// that order — never sorted by <c>SentAt</c>. That is what lets these tests prove
/// the runtime assembles history in ingestion order using timestamps that are
/// deliberately out of order.
/// </remarks>
internal sealed class FakeWebChatWorld : IWebChatEndpointRepository, IWebChatRuntimeStore, IConversationRepository
{
    private readonly List<WebChatEndpoint> _endpoints = [];
    private readonly Dictionary<Guid, string> _agentNames = [];
    private readonly Dictionary<Guid, AiAgentStatus> _agentStatuses = [];
    private readonly List<Conversation> _conversations = [];
    private readonly List<WebChatSession> _sessions = [];
    private readonly List<ConversationTurn> _turns = [];
    private readonly List<StoredMessage> _messages = [];
    private readonly Lock _sync = new();

    private long _sequence;

    private sealed record StoredMessage(
        Guid Id,
        ConversationId ConversationId,
        MessageSender Sender,
        string Text,
        DateTimeOffset SentAt,
        long Sequence);

    /// <summary>Adds an agent the world knows about.</summary>
    public void AddAgent(AiAgentId id, string name, AiAgentStatus status)
    {
        _agentNames[id.Value] = name;
        _agentStatuses[id.Value] = status;
    }

    /// <summary>Changes an agent's status, as an administrator would mid-conversation.</summary>
    public void SetAgentStatus(AiAgentId id, AiAgentStatus status) =>
        _agentStatuses[id.Value] = status;

    /// <summary>Adds an endpoint the world knows about.</summary>
    public WebChatEndpoint AddEndpoint(WebChatEndpoint endpoint)
    {
        _endpoints.Add(endpoint);

        return endpoint;
    }

    /// <summary>Every message stored, in ingestion order.</summary>
    public IReadOnlyList<(MessageSender Sender, string Text)> Transcript(ConversationId conversation)
    {
        lock (_sync)
        {
            return
            [
                .. _messages
                    .Where(message => message.ConversationId == conversation)
                    .OrderBy(message => message.Sequence)
                    .Select(message => (message.Sender, message.Text)),
            ];
        }
    }

    /// <summary>Stores a message directly, for arranging a history.</summary>
    /// <remarks>
    /// Takes an explicit <paramref name="sentAt"/> so a test can build a history
    /// whose timestamps are deliberately not chronological.
    /// </remarks>
    public void SeedMessage(
        ConversationId conversation,
        MessageSender sender,
        string text,
        DateTimeOffset sentAt)
    {
        lock (_sync)
        {
            _messages.Add(new StoredMessage(
                Guid.CreateVersion7(),
                conversation,
                sender,
                text,
                sentAt,
                ++_sequence));
        }
    }

    /// <summary>The turns recorded, for asserting idempotency.</summary>
    public IReadOnlyList<ConversationTurn> Turns
    {
        get
        {
            lock (_sync)
            {
                return [.. _turns];
            }
        }
    }

    // ---- IWebChatEndpointRepository -------------------------------------

    public Task<ConcurrencyToken> AddAsync(
        WebChatEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        _endpoints.Add(endpoint);

        return Task.FromResult(ConcurrencyToken.From("v1"));
    }

    public Task<Versioned<WebChatEndpoint>?> FindAsync(
        TenantId tenantId,
        WebChatEndpointId endpointId,
        CancellationToken cancellationToken = default)
    {
        WebChatEndpoint? found = _endpoints.FirstOrDefault(
            endpoint => endpoint.Id == endpointId && endpoint.TenantId == tenantId);

        return Task.FromResult(found is null
            ? null
            : new Versioned<WebChatEndpoint>(found, ConcurrencyToken.From("v1")));
    }

    public Task<IReadOnlyList<WebChatEndpointRow>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WebChatEndpointRow> rows =
        [
            .. _endpoints
                .Where(endpoint => endpoint.TenantId == tenantId)
                .Select(endpoint => new WebChatEndpointRow(
                    endpoint.Id.Value,
                    endpoint.AiAgentId.Value,
                    _agentNames.GetValueOrDefault(endpoint.AiAgentId.Value, "Agent"),
                    _agentStatuses.GetValueOrDefault(endpoint.AiAgentId.Value, AiAgentStatus.Draft),
                    endpoint.PublicKey.Value,
                    endpoint.Status,
                    endpoint.CreatedAt,
                    endpoint.UpdatedAt)),
        ];

        return Task.FromResult(rows);
    }

    public Task<ConcurrencyToken> UpdateAsync(
        WebChatEndpoint endpoint,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConcurrencyToken.From("v2"));

    public Task<PublicWebChatEndpoint?> FindByPublicKeyAsync(
        WebChatPublicKey publicKey,
        CancellationToken cancellationToken = default)
    {
        WebChatEndpoint? found = _endpoints.FirstOrDefault(
            endpoint => endpoint.PublicKey == publicKey);

        return Task.FromResult(found is null
            ? null
            : new PublicWebChatEndpoint(
                found.Id,
                found.TenantId,
                found.AiAgentId,
                _agentNames.GetValueOrDefault(found.AiAgentId.Value, "Agent"),
                _agentStatuses.GetValueOrDefault(found.AiAgentId.Value, AiAgentStatus.Draft),
                found.Status));
    }

    public Task<bool> ExistsForAgentAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_endpoints.Any(
            endpoint => endpoint.TenantId == tenantId && endpoint.AiAgentId == aiAgentId));

    // ---- IWebChatRuntimeStore -------------------------------------------

    public Task StartConversationAsync(
        Conversation conversation,
        WebChatSession session,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _conversations.Add(conversation);
            _sessions.Add(session);
        }

        return Task.CompletedTask;
    }

    public Task<WebChatSession?> FindSessionAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(
                _sessions.FirstOrDefault(session => session.ConversationId == conversationId));
        }
    }

    public Task<ConversationTurn?> FindTurnAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_turns.FirstOrDefault(turn =>
                turn.ConversationId == conversationId
                && turn.ClientMessageId == clientMessageId));
        }
    }

    public Task<ConversationTurn?> BeginTurnAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        string text,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            // The unique index, modelled. Two requests with the same client id
            // cannot both open a turn, whatever order they arrive in.
            if (_turns.Any(turn =>
                turn.ConversationId == conversationId
                && turn.ClientMessageId == clientMessageId))
            {
                return Task.FromResult<ConversationTurn?>(null);
            }

            Guid messageId = Guid.CreateVersion7();

            _messages.Add(new StoredMessage(
                messageId,
                conversationId,
                MessageSender.Customer,
                text,
                sentAt,
                ++_sequence));

            ConversationTurn turn = ConversationTurn.Begin(
                conversationId,
                clientMessageId,
                MessageId.From(messageId),
                sentAt);

            _turns.Add(turn);

            return Task.FromResult<ConversationTurn?>(turn);
        }
    }

    public Task<ConversationMessageRef> CompleteTurnAsync(
        ConversationTurn turn,
        string replyText,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Guid messageId = Guid.CreateVersion7();

            StoredMessage reply = new(
                messageId,
                turn.ConversationId,
                MessageSender.AiAgent,
                replyText,
                sentAt,
                ++_sequence);

            _messages.Add(reply);

            turn.Complete(MessageId.From(messageId), sentAt);

            return Task.FromResult(new ConversationMessageRef(
                reply.Id,
                reply.Text,
                reply.SentAt,
                reply.Sequence));
        }
    }

    public Task FailTurnAsync(
        ConversationTurn turn,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default)
    {
        turn.Fail(failedAt);

        return Task.CompletedTask;
    }

    public Task<ConversationMessageRef?> FindReplyAsync(
        ConversationTurn turn,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(turn.ReplyMessageId is MessageId reply ? Find(reply.Value) : null);

    public Task<ConversationMessageRef?> FindCustomerMessageAsync(
        ConversationTurn turn,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(turn.CustomerMessageId.Value));

    private ConversationMessageRef? Find(Guid messageId)
    {
        lock (_sync)
        {
            StoredMessage? message = _messages.FirstOrDefault(stored => stored.Id == messageId);

            return message is null
                ? null
                : new ConversationMessageRef(
                    message.Id,
                    message.Text,
                    message.SentAt,
                    message.Sequence);
        }
    }

    // ---- IConversationRepository ----------------------------------------

    public Task<ConversationPage> ListAsync(
        TenantId tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConversationPage([], page, pageSize, 0));

    public Task<ConversationDetail?> FindAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ConversationDetail?>(null);

    public Task<bool> BelongsToAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_conversations.Any(conversation =>
            conversation.Id == conversationId && conversation.TenantId == tenantId));

    public Task<ConversationMessagePage> ListMessagesAsync(
        ConversationId conversationId,
        long? afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            List<ConversationMessageRecord> rows =
            [
                .. _messages
                    .Where(message => message.ConversationId == conversationId
                        && (afterSequence is not long cursor || message.Sequence > cursor))
                    .OrderBy(message => message.Sequence)
                    .Take(limit + 1)
                    .Select(Record),
            ];

            bool hasMore = rows.Count > limit;

            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            return Task.FromResult(new ConversationMessagePage(
                rows,
                hasMore && rows.Count > 0 ? rows[^1].IngestionSequence : null));
        }
    }

    public Task<IReadOnlyList<ConversationMessageRecord>> ListRecentMessagesAsync(
        ConversationId conversationId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            // Newest `limit` by sequence, handed back oldest-first. Exactly what
            // PostgreSQL does, and never a sort by SentAt.
            IReadOnlyList<ConversationMessageRecord> rows =
            [
                .. _messages
                    .Where(message => message.ConversationId == conversationId)
                    .OrderByDescending(message => message.Sequence)
                    .Take(limit)
                    .OrderBy(message => message.Sequence)
                    .Select(Record),
            ];

            return Task.FromResult(rows);
        }
    }

    public Task<AiAgentId?> FindBoundAgentAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            WebChatSession? session = _sessions.FirstOrDefault(
                candidate => candidate.ConversationId == conversationId);

            return Task.FromResult(session is null ? null : (AiAgentId?)session.AiAgentId);
        }
    }

    private static ConversationMessageRecord Record(StoredMessage message) =>
        new(message.Id, message.Sender, message.Text, message.SentAt, message.Sequence);
}
