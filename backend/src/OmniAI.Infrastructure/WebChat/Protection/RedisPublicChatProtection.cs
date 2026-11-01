using System.Globalization;
using StackExchange.Redis;
using OmniAI.Application.WebChat.Protection;
using OmniAI.Domain.Conversations;
using OmniAI.Domain.Tenants;
using OmniAI.Domain.WebChat;

namespace OmniAI.Infrastructure.WebChat.Protection;

/// <summary>
/// The public-chat limits, counted in Redis.
/// </summary>
/// <remarks>
/// <strong>Why Redis and not a process.</strong> This platform will run more than
/// one API instance. A per-process limiter is correct until the second instance
/// starts and then fails silently — no error, no failing test, just twice the
/// traffic allowed, then three times. A limit that stops being a limit as the
/// deployment grows is worse than no limit, because everybody believes it is
/// still there. Redis is the smallest thing that can hold a counter every
/// instance agrees on.
/// <para>
/// <strong>Why not PostgreSQL, which is already shared.</strong> These counters
/// are written on every public request, are worthless within minutes, and must
/// never be the reason a request waits. Putting them in the transactional store
/// would mean row contention and vacuum pressure on the platform's system of
/// record in exchange for durability nobody wants: if the counters were lost, the
/// correct behaviour is to start counting again, which is exactly what an
/// in-memory store with TTLs does natively.
/// </para>
/// <para>
/// <strong>Why not <c>IDistributedCache</c> or a generic Redis service.</strong>
/// Both would have moved the counting policy out of this file and into whichever
/// handler composed the calls. The Application asks three questions with names;
/// this class is the only thing in the platform that knows what a key, a window
/// or a script is.
/// </para>
/// <para>
/// <strong>One script per decision, and it is the whole reason this is
/// safe.</strong> Several limits can apply to one action. Evaluating them with
/// separate round trips would consume the early ones before discovering a later
/// one refuses — so a caller already over their workspace budget would still burn
/// their conversation allowance on every rejected attempt, and two racing
/// requests could both read "under the limit" before either incremented. The
/// script inspects every counter and then consumes every counter, atomically,
/// inside Redis. Concurrency is settled by Redis, not by how the .NET tasks
/// happened to interleave.
/// </para>
/// <para>
/// <strong>It fails closed and cannot do otherwise.</strong> Every Redis failure
/// becomes <see cref="PublicChatProtectionUnavailableException"/>. There is no
/// branch here that returns an allowed decision without a successful script
/// evaluation, so an unreachable Redis cannot let traffic through however the
/// callers are written.
/// </para>
/// <para>
/// <strong>What is deliberately not stored.</strong> No address, no credential,
/// no message text, no email, no agent instructions. The keys hold a pseudonymous
/// network digest, a tenant id, an endpoint id and a conversation id, and the
/// values are integers. Redis here is protection state, not a second copy of the
/// business.
/// </para>
/// </remarks>
internal sealed class RedisPublicChatProtection : IPublicChatProtection
{
    /// <summary>
    /// Inspect every counter, then consume every counter — or none of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>KEYS</c> are the counters. <c>ARGV</c> holds a limit and a window in
    /// seconds for each, in the same order: <c>ARGV[2i-1]</c> is the limit for
    /// <c>KEYS[i]</c> and <c>ARGV[2i]</c> its window.
    /// </para>
    /// <para>
    /// <strong>Two passes, not one.</strong> The first pass only reads. Only once
    /// every counter has room does the second pass increment, which is what makes
    /// a rejection cost nothing anywhere. Interleaving them would leave the
    /// counters before the offending one permanently inflated by refused
    /// attempts.
    /// </para>
    /// <para>
    /// <strong>An anchored window, and the boundary behaviour is known.</strong>
    /// The expiry is set only when a counter is created, so a window begins at
    /// the first request in it and is never extended by later ones — a sliding
    /// expiry would let sustained traffic keep a full counter alive forever and
    /// turn a one-minute limit into a permanent lockout. The consequence is the
    /// usual fixed-window one: a caller who spends their allowance at the end of
    /// one window and again at the start of the next achieves up to twice the
    /// limit across the boundary. For the job these limits do — bounding cost and
    /// abuse, not metering — that is a factor of two on a number chosen with far
    /// more headroom than that, and it buys an implementation with no per-request
    /// sorted-set writes and no clock arithmetic to get wrong.
    /// </para>
    /// <para>
    /// <strong>Retry-After is the truth, not a guess.</strong> On refusal the
    /// script returns the remaining life of the counter that refused, so a client
    /// waits exactly as long as it must. <c>PTTL</c> answers -1 for a key with no
    /// expiry and -2 for one that has just vanished; both fall back to a full
    /// window, which over-waits rather than inviting an immediate retry.
    /// </para>
    /// <para>
    /// Sent as <c>EVAL</c> rather than managed as a stored script. It is a few
    /// hundred bytes against a local Redis, the saving is not measurable next to
    /// the round trip, and it removes a whole failure mode — a <c>NOSCRIPT</c>
    /// after a Redis restart or a failover to a replica that never loaded it.
    /// </para>
    /// </remarks>
    internal const string DecisionScript = """
        local total = #KEYS
        for index = 1, total do
          local limit = tonumber(ARGV[2 * index - 1])
          local current = tonumber(redis.call('GET', KEYS[index]) or '0')
          if current + 1 > limit then
            local remaining = redis.call('PTTL', KEYS[index])
            if remaining == nil or remaining < 0 then
              remaining = tonumber(ARGV[2 * index]) * 1000
            end
            return {0, remaining}
          end
        end
        for index = 1, total do
          local consumed = redis.call('INCR', KEYS[index])
          if consumed == 1 then
            redis.call('EXPIRE', KEYS[index], tonumber(ARGV[2 * index]))
          end
        end
        return {1, 0}
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly PublicChatProtectionOptions _limits;

    public RedisPublicChatProtection(
        IConnectionMultiplexer redis,
        PublicChatProtectionOptions limits)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public Task<PublicChatDecision> AuthoriseConversationStartAsync(
        ClientNetworkKey client,
        WebChatEndpointId endpointId,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            [
                // Per network *and* per endpoint. Rotating addresses defeats the
                // first; nothing defeats the second, which is the point of having
                // both.
                new Counter(
                    Key($"start:client:{client.Value}:{endpointId.Value:N}"),
                    _limits.ConversationStartsPerClient),
                new Counter(
                    Key($"start:endpoint:{endpointId.Value:N}"),
                    _limits.ConversationStartsPerEndpoint),
            ],
            cancellationToken);

    public Task<PublicChatDecision> AuthoriseRequestAsync(
        ClientNetworkKey client,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            [
                // Not partitioned by endpoint or workspace. This one is about the
                // cost of serving a request at all, which this deployment bears
                // regardless of who the request was aimed at.
                new Counter(Key($"request:client:{client.Value}"), _limits.RequestsPerClient),
            ],
            cancellationToken);

    public Task<PublicChatDecision> AuthoriseGenerationAsync(
        TenantId tenantId,
        ConversationId conversationId,
        CancellationToken cancellationToken = default) =>
        DecideAsync(
            [
                // One session with a valid credential is still bounded: a script
                // driving a legitimate conversation is the cheapest abuse there
                // is.
                new Counter(
                    Key($"generate:conversation:{conversationId.Value:N}"),
                    _limits.MessagesPerConversation),

                // Three windows on the workspace, evaluated together. Burst caps
                // how fast money goes; sustained stops burst-after-burst; the
                // daily ceiling is the one an operator would recognise on an
                // invoice.
                new Counter(
                    Key($"generate:tenant:{tenantId.Value:N}:burst"),
                    _limits.GenerationsPerWorkspaceBurst),
                new Counter(
                    Key($"generate:tenant:{tenantId.Value:N}:sustained"),
                    _limits.GenerationsPerWorkspaceSustained),
                new Counter(
                    Key($"generate:tenant:{tenantId.Value:N}:daily"),
                    _limits.GenerationsPerWorkspaceDaily),
            ],
            cancellationToken);

    /// <summary>
    /// Evaluates every counter for one action and consumes them together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Cancellation is not passed to Redis, and that is deliberate.</strong>
    /// StackExchange.Redis has no per-command cancellation; a token would only be
    /// checked before dispatch, and abandoning a command whose script may already
    /// have consumed the counters would leave the platform unsure whether the
    /// budget was spent. The token is honoured before the call and then the
    /// decision is allowed to complete, bounded by the client's own timeout.
    /// </para>
    /// <para>
    /// Every applicable counter is in one script, so the multi-key evaluation
    /// assumes a single Redis instance or a primary/replica pair — not a cluster,
    /// where these keys would need hash tags to share a slot. That is recorded as
    /// a known constraint rather than pre-emptively engineered around.
    /// </para>
    /// </remarks>
    private async Task<PublicChatDecision> DecideAsync(
        Counter[] counters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RedisKey[] keys = new RedisKey[counters.Length];
        RedisValue[] values = new RedisValue[counters.Length * 2];

        for (int index = 0; index < counters.Length; index++)
        {
            keys[index] = counters[index].Key;
            values[(index * 2) + 0] = counters[index].Window.Limit;
            values[(index * 2) + 1] = (long)Math.Ceiling(
                counters[index].Window.Window.TotalSeconds);
        }

        RedisResult result;

        try
        {
            result = await _redis.GetDatabase()
                .ScriptEvaluateAsync(DecisionScript, keys, values)
                .ConfigureAwait(false);
        }
        catch (Exception failure) when (IsProtectionStoreFailure(failure, cancellationToken))
        {
            // The one translation in this class, and it is the fail-closed path.
            // The caller cannot proceed on an unknown answer because there is no
            // unknown answer to return.
            throw new PublicChatProtectionUnavailableException(
                "The public chat protection store could not be reached, so the request was "
                + "refused rather than served without a rate limit. "
                + failure.GetType().Name);
        }

        return Interpret(result);
    }

    /// <summary>
    /// Reads the script's two-element answer.
    /// </summary>
    /// <remarks>
    /// A malformed result is treated as a store failure rather than as an
    /// allowance. It should be unreachable — the script has exactly two return
    /// statements and both return a pair — and if it ever is reached, refusing is
    /// the only safe reading.
    /// </remarks>
    private static PublicChatDecision Interpret(RedisResult result)
    {
        long[]? answer = (long[]?)result;

        if (answer is not { Length: 2 })
        {
            throw new PublicChatProtectionUnavailableException(
                "The public chat protection store returned an answer this platform could not "
                + "read, so the request was refused.");
        }

        return answer[0] == 1
            ? PublicChatDecision.Allow
            : PublicChatDecision.Reject(TimeSpan.FromMilliseconds(answer[1]));
    }

    /// <summary>
    /// Whether a failure means the protection store is unusable right now.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. A Redis connection failure, a timeout, a server-side
    /// error and a disposed multiplexer are all "cannot tell whether this is
    /// allowed"; anything else is a bug in this code and must not be dressed up as
    /// an outage, because doing so would hide it behind a message that tells
    /// operators to look at Redis.
    /// <para>
    /// <strong><c>IOException</c> included from Phase 34.</strong> With Redis
    /// paused (accepting TCP, never answering), the script call surfaced
    /// "Unable to read data from the transport connection" as a bare
    /// <c>IOException</c> over a <c>SocketException</c>, which escaped as a 500.
    /// It still failed closed — nothing had been written — but the caller got a
    /// server error instead of the refusal this class promises. It is only ever
    /// raised here by the connection to the store, so it means the same thing as
    /// the others.
    /// </para>
    /// <para>
    /// <strong>A cancellation the caller did not ask for is one too.</strong> The
    /// same drill produced a <c>TaskCanceledException</c> from the script call: the
    /// client cancels in-flight commands when it abandons a stalled connection.
    /// The call is not given the caller's token, so such a cancellation cannot be
    /// the visitor giving up — it is the store being unusable. A cancellation while
    /// the caller's own token is cancelled is left alone: that request was
    /// abandoned, not refused.
    /// </para>
    /// </remarks>
    internal static bool IsProtectionStoreFailure(Exception failure, CancellationToken caller = default) =>
        failure is RedisException or ObjectDisposedException or TimeoutException
            or System.Net.Sockets.SocketException or System.IO.IOException
        || (failure is OperationCanceledException && !caller.IsCancellationRequested);

    /// <summary>
    /// One fully-qualified counter name.
    /// </summary>
    /// <remarks>
    /// The prefix carries a schema version, so changing what a counter means can
    /// start a new generation of keys rather than inheriting values counted under
    /// the old rules. It is configurable for the same reason, and because it is
    /// what lets parallel test classes share one Redis without seeing each other's
    /// counts.
    /// </remarks>
    private string Key(string suffix) =>
        string.Create(CultureInfo.InvariantCulture, $"{_limits.KeyPrefix}:{suffix}");

    /// <summary>One counter and the policy that applies to it.</summary>
    private readonly record struct Counter(string Key, RateLimitWindow Window);
}
