using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Application.WebChat.Protection;

/// <summary>
/// Whether one public action may proceed, and how long to wait if not.
/// </summary>
/// <param name="Allowed">Whether the action may go ahead.</param>
/// <param name="RetryAfter">
/// How long until the limiting counter frees up. Meaningful only on a refusal,
/// where it is the remaining life of whichever counter refused — the shortest
/// honest answer to "when can I try again", rather than a guess or a fixed
/// number.
/// </param>
public sealed record PublicChatDecision(bool Allowed, TimeSpan RetryAfter)
{
    /// <summary>The action may proceed, and its counters have been consumed.</summary>
    public static PublicChatDecision Allow { get; } = new(true, TimeSpan.Zero);

    /// <summary>
    /// The action is refused, and nothing was consumed.
    /// </summary>
    /// <remarks>
    /// A refusal that had already spent some of the counters would punish a
    /// rejected caller twice and, worse, would let a caller who is over one limit
    /// drain every other limit by retrying. See <see cref="IPublicChatProtection"/>
    /// on why the whole decision is atomic.
    /// </remarks>
    public static PublicChatDecision Reject(TimeSpan retryAfter) =>
        new(false, retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter);
}

/// <summary>
/// The gate in front of the only surface where a stranger can spend a workspace's
/// money.
/// </summary>
/// <remarks>
/// <strong>The Application asks a question; Infrastructure decides how to answer
/// it.</strong> Nothing above the adapter knows there is a Redis, a Lua script, a
/// counter or a window — only that some public actions may be refused with a
/// retry hint. That is why this is three named questions rather than an
/// <c>IRedisService</c> or an <c>IDistributedCache</c>: a generic store would have
/// let the counting policy leak into handlers one <c>Increment</c> at a time, and
/// a handler that composes its own limits is a handler that can compose them
/// wrongly.
/// <para>
/// <strong>Each method is one atomic decision over several counters.</strong> A
/// single logical action can be subject to more than one limit — a start is
/// bounded per network *and* per endpoint; a generation is bounded per
/// conversation *and* three ways per workspace. Evaluating those one at a time
/// would consume the earlier ones before discovering the later one refuses,
/// which turns a rejected request into a request that still cost the caller
/// their allowance everywhere else. Every implementation must inspect all of its
/// counters, and consume all or none.
/// </para>
/// <para>
/// <strong>It fails closed, by construction.</strong> There is no return value
/// meaning "could not tell". An implementation that cannot reach its store
/// throws <see cref="PublicChatProtectionUnavailableException"/>, so an
/// unreachable protection store cannot produce an allowed decision however the
/// calling code is written. That is deliberate: the alternative — an in-memory
/// fallback — would mean the platform's abuse protection quietly switched itself
/// off at exactly the moment it was under strain, and per-process counters would
/// be wrong the instant there were two instances anyway.
/// </para>
/// <para>
/// <strong>It is not the conversation lock, and does not replace it.</strong> The
/// PostgreSQL advisory lock serialises generation inside one conversation so two
/// messages cannot be answered against the same history at once. This bounds how
/// much may be generated at all. They solve different problems and both are
/// required.
/// </para>
/// </remarks>
public interface IPublicChatProtection
{
    /// <summary>
    /// Whether a visitor may start a new conversation at this endpoint.
    /// </summary>
    /// <remarks>
    /// Bounded per network *and* in aggregate per endpoint. The first stops one
    /// machine manufacturing conversations; the second stops a thousand machines
    /// each staying politely under the first.
    /// </remarks>
    Task<PublicChatDecision> AuthoriseConversationStartAsync(
        ClientNetworkKey client,
        WebChatEndpointId endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a network may make another public write request at all.
    /// </summary>
    /// <remarks>
    /// The cheap outer boundary, consumed before any credential is believed and
    /// before anything is looked up — so a flood of requests that were going to
    /// be refused costs a counter rather than a query. It deliberately counts
    /// requests, including duplicates and including ones that turn out to be
    /// unauthorised: this is anti-flood, not accounting.
    /// </remarks>
    Task<PublicChatDecision> AuthoriseRequestAsync(
        ClientNetworkKey client,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether one new AI generation attempt may be made.
    /// </summary>
    /// <remarks>
    /// <strong>Consumed once per new logical turn, never per request.</strong> The
    /// caller must have already established that this is a genuinely new turn —
    /// after the idempotency check, and inside the conversation lock — because a
    /// duplicate, a network retry or a replayed completed turn makes no provider
    /// call and must not spend any of the budget. A single message that is sent a
    /// hundred times shows up here exactly once.
    /// <para>
    /// <strong>A failed provider attempt still counts.</strong> The consumption
    /// happens before the call and is not refunded when it fails, which is
    /// intentional: an attacker who could make attempts fail — with oversized
    /// context, or by exhausting an upstream — would otherwise have found an
    /// unlimited free lane through the safety budget. The workspace made an
    /// external attempt either way.
    /// </para>
    /// <para>
    /// This is a count of attempts, not of tokens, requests-in-flight or money.
    /// Provider usage accounting and billing do not exist yet; this exists only
    /// so that their absence is not unbounded.
    /// </para>
    /// </remarks>
    Task<PublicChatDecision> AuthoriseGenerationAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default);
}
