using OmniAI.Application.Concurrency;
using OmniAI.Domain.AiAgents;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Customers;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Persistence;

/// <summary>One endpoint, as the admin list publishes it.</summary>
/// <remarks>
/// Carries the bound agent's name and status because a list of endpoints is
/// useless without them: "which agent answers here, and can it?" is the question
/// somebody opens this screen to ask.
/// </remarks>
public sealed record WebChatEndpointRow(
    Guid Id,
    Guid AiAgentId,
    string AiAgentName,
    AiAgentStatus AiAgentStatus,
    string PublicKey,
    WebChatEndpointStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// What a public request needs to know about the endpoint it arrived at.
/// </summary>
/// <remarks>
/// <strong>Assembled inside the data layer so a public caller never causes two
/// lookups whose results could disagree.</strong> It carries the agent's display
/// name and status and nothing else about the agent — no instructions, no
/// identity beyond what the runtime needs. The tenant id is present because the
/// server must know which workspace it is acting for; it is never published.
/// <para>
/// <strong>Phase 37.5a (owner decision D-3) adds two things a visitor already
/// sees or should:</strong> the workspace's display name, so a visitor knows whose
/// chat it is, and the widget's appearance, which the embedded read already
/// published and the hosted page now shows too. Nothing else about the workspace
/// is read: no slug, status, plan, member or other endpoint.
/// </para>
/// </remarks>
public sealed record PublicWebChatEndpoint(
    WebChatEndpointId Id,
    TenantId TenantId,
    AiAgentId AiAgentId,
    string AiAgentName,
    AiAgentStatus AiAgentStatus,
    WebChatEndpointStatus Status)
{
    /// <summary>The workspace's display name, as its owner set it.</summary>
    public string WorkspaceName { get; init; } = string.Empty;

    /// <summary>The endpoint's appearance: greeting, accent, corner and theme.</summary>
    public WebChatWidgetSettings Widget { get; init; } = WebChatWidgetSettings.Default;
}

/// <summary>
/// Storing and reading website chat endpoints.
/// </summary>
public interface IWebChatEndpointRepository
{
    /// <summary>Stores a new endpoint and returns the version it was written at.</summary>
    Task<ConcurrencyToken> AddAsync(
        WebChatEndpoint endpoint,
        CancellationToken cancellationToken = default);

    /// <summary>One endpoint of this workspace, with the version it was read at.</summary>
    Task<Versioned<WebChatEndpoint>?> FindAsync(
        TenantId tenantId,
        WebChatEndpointId endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>This workspace's endpoints, newest first.</summary>
    /// <remarks>
    /// Unpaged, deliberately. A workspace has a handful of chat endpoints — one
    /// per site, at most — and paging a list that never grows past single digits
    /// would be ceremony. If that assumption ever breaks it will break loudly, in
    /// a screen that is obviously too long, rather than silently.
    /// </remarks>
    Task<IReadOnlyList<WebChatEndpointRow>> ListAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Saves a lifecycle change, if the endpoint is still at the expected version.</summary>
    /// <exception cref="WebChatEndpointVersionMismatchException">
    /// Somebody else changed it first.
    /// </exception>
    Task<ConcurrencyToken> UpdateAsync(
        WebChatEndpoint endpoint,
        ConcurrencyToken expected,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The endpoint a public key names, with its agent, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The only lookup a public caller can cause. It takes no tenant, because a
    /// public caller has none to give and could not be believed if they did.
    /// </remarks>
    Task<PublicWebChatEndpoint?> FindByPublicKeyAsync(
        WebChatPublicKey publicKey,
        CancellationToken cancellationToken = default);

    /// <summary>Whether this workspace already has an endpoint for this agent.</summary>
    /// <remarks>
    /// One endpoint per agent, so a workspace cannot accumulate several public
    /// links to the same agent that all have to be remembered and disabled
    /// separately. The database enforces it; this answers the question early so
    /// the caller gets a sentence rather than a constraint violation.
    /// </remarks>
    Task<bool> ExistsForAgentAsync(
        TenantId tenantId,
        AiAgentId aiAgentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The write path behind a live website chat conversation.
/// </summary>
/// <remarks>
/// <strong>Not a repository, and named so it cannot be mistaken for one.</strong>
/// Every operation here is a transaction with a rule inside it — a conversation
/// and its access binding written together, a customer message and its turn
/// written together, a reply and its completion written together. Exposing
/// <c>Add</c> and <c>Save</c> instead would let a caller write half of any of
/// those, and the halves are exactly what must never exist: a conversation nobody
/// can reach, a turn with no message, a reply that was billed for and not stored.
/// </remarks>
public interface IWebChatRuntimeStore
{
    /// <summary>
    /// Starts a conversation, the customer it belongs to and the anonymous claim
    /// on it, together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One transaction. A conversation without its session would be a conversation
    /// its own visitor could never reach again, a session without its conversation
    /// would admit somebody to nothing, and a conversation without its customer
    /// could not be written at all — the database requires one.
    /// </para>
    /// <para>
    /// The customer is created here rather than looked up, because a public web
    /// visitor arrives with no identity this platform has ever seen. That is the
    /// honest answer and not a gap to be filled: there is no durable web identity
    /// to key on, and inventing one from a browser, a network address or a stored
    /// marker would be identity resolution by fingerprint.
    /// </para>
    /// </remarks>
    Task StartConversationAsync(
        Conversation conversation,
        Customer customer,
        WebChatSession session,
        CancellationToken cancellationToken = default);

    /// <summary>The anonymous claim on a conversation, or <see langword="null"/>.</summary>
    Task<WebChatSession?> FindSessionAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The turn a client message id names in this conversation, if there is one.
    /// </summary>
    Task<ConversationTurn?> FindTurnAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the customer's message and opens a turn for it, together.
    /// </summary>
    /// <remarks>
    /// <strong>Before the provider is called, and committed before it is
    /// called.</strong> What the customer said is a fact the moment they said it,
    /// and it must survive the reply failing. Committing first is also what makes
    /// the turn visible to a concurrent duplicate, which is how the second request
    /// discovers it has nothing to do.
    /// </remarks>
    /// <returns>The stored turn, or <see langword="null"/> if this client message
    /// id was already taken — a duplicate that arrived at the same moment.</returns>
    Task<ConversationTurn?> BeginTurnAsync(
        ConversationId conversationId,
        ClientMessageId clientMessageId,
        string text,
        DateTimeOffset sentAt,
        string[]? attachments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the reply and settles the turn as completed, together.
    /// </summary>
    /// <returns>The stored reply.</returns>
    Task<ConversationMessageRef> CompleteTurnAsync(
        ConversationTurn turn,
        string replyText,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles the turn as failed, leaving the customer's message stored.
    /// </summary>
    Task FailTurnAsync(
        ConversationTurn turn,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Who is answering this conversation right now.
    /// </summary>
    /// <remarks>
    /// Read inside the conversation lock, immediately before the runtime decides
    /// whether to spend anything. Reading it earlier would be reading a value a
    /// takeover is allowed to change while the request queues for the lock — and
    /// acting on it would be generating a reply for a conversation somebody has
    /// already taken over.
    /// </remarks>
    /// <returns>
    /// <see langword="null"/> when there is no such conversation, which the
    /// caller has already excluded by authorising a session on it.
    /// </returns>
    Task<ConversationHandling?> ReadHandlingAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles the turn as completed with no reply, because a person is handling
    /// this conversation.
    /// </summary>
    /// <remarks>
    /// The customer's message stays exactly where <see cref="BeginTurnAsync"/>
    /// put it. Nothing was retrieved, nothing was generated and nothing was
    /// spent; the turn is closed so a retry of the same client message id replays
    /// this same outcome instead of starting again.
    /// </remarks>
    Task SettleWithoutReplyAsync(
        ConversationTurn turn,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken = default);

    /// <summary>The stored reply of a settled turn, for answering a duplicate.</summary>
    Task<ConversationMessageRef?> FindReplyAsync(
        ConversationTurn turn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The customer message a turn was started by, for answering a duplicate.
    /// </summary>
    /// <remarks>
    /// Read back rather than remembered, because a duplicate arrives in a request
    /// that never held the original text. Replaying the stored row is what makes a
    /// repeat return the same answer as the first attempt rather than a
    /// reconstruction of it.
    /// </remarks>
    Task<ConversationMessageRef?> FindCustomerMessageAsync(
        ConversationTurn turn,
        CancellationToken cancellationToken = default);
}

/// <summary>A stored message, as the runtime hands it back.</summary>
public sealed record ConversationMessageRef(
    Guid Id,
    string Text,
    DateTimeOffset SentAt,
    long IngestionSequence);
