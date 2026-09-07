namespace OmniAI.Application.WebChat.Protection;

/// <summary>
/// A public caller has been refused because they are asking too often.
/// </summary>
/// <remarks>
/// <strong>The message is written for a stranger, and says nothing.</strong> No
/// quota, no window, no counter name, no current usage, no workspace, no address,
/// and no hint about which of the layered limits was reached. All of those would
/// be a map: somebody probing the surface learns exactly how many requests a
/// minute they may make, from how many networks, before anybody notices. What a
/// visitor needs is that nothing is broken and that waiting works, which is what
/// this says.
/// <para>
/// <strong>It carries how long to wait, and the edge publishes that.</strong> A
/// bare refusal invites a client to retry immediately and repeatedly, which is
/// the behaviour the refusal exists to stop.
/// <see cref="RetryAfter"/> becomes a <c>Retry-After</c> header, so a
/// well-behaved client waits and a person is told roughly how long. Disclosing
/// the remaining life of a counter is not disclosing the policy: it says when,
/// not how much.
/// </para>
/// <para>
/// <strong>Nothing retries it automatically.</strong> Not the client, not the
/// server. A retry of a paid request is a decision, and a platform that retried
/// through its own rate limit would be arguing with itself.
/// </para>
/// </remarks>
public sealed class PublicChatRateLimitedException(TimeSpan retryAfter)
    : Exception("Too many requests were made. Wait a moment and try again.")
{
    /// <summary>How long until the limiting counter frees up.</summary>
    public TimeSpan RetryAfter { get; } = retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;
}

/// <summary>
/// A public write could not be protected, so it was not performed.
/// </summary>
/// <remarks>
/// <strong>This is the fail-closed path, and it is a feature.</strong> It is
/// raised when the protection store cannot be reached, when this deployment has
/// none configured, and when no trustworthy client address could be established.
/// In every one of those cases the platform can still perform the action
/// perfectly well — it simply cannot tell whether it should, and performing an
/// unbounded number of paid, data-creating operations for anonymous strangers
/// because a counter was unavailable is the one outcome that must not be
/// possible.
/// <para>
/// <strong>Why not fall back to an in-memory limiter.</strong> Two reasons, and
/// either is sufficient. It would be wrong the moment there is a second API
/// instance, which is the deployment this platform is heading for — and being
/// wrong silently is worse than being unavailable loudly. And a fallback that
/// engages exactly when the shared store is struggling engages exactly when the
/// platform is most likely to be under attack.
/// </para>
/// <para>
/// <strong>What it does not take down.</strong> Only public writes — starting a
/// conversation and sending a message. Reading a chat endpoint's public face and
/// reading a transcript create nothing and cost no provider call, so they are not
/// gated on this and keep working. Neither is anything behind the admin
/// boundary: signing in, team administration, agent configuration and reading
/// conversations are untouched, because a member of a workspace is not an
/// anonymous stranger and is not rate-limited by a policy written for one.
/// </para>
/// <para>
/// The message tells a visitor the chat is temporarily unavailable. It does not
/// mention Redis, a counter store, configuration or a missing setting; the
/// operator's copy of that is in the log.
/// </para>
/// </remarks>
public sealed class PublicChatProtectionUnavailableException(string reason)
    : Exception(
        "This chat is temporarily unavailable. Please try again shortly, or contact the "
        + "business another way.")
{
    /// <summary>
    /// Why protection could not be applied, for the operator.
    /// </summary>
    /// <remarks>
    /// Deliberately not the exception's message. The message goes to the visitor
    /// and must stay uninformative; this goes to the log, where naming the
    /// missing setting or the unreachable dependency is the whole point. It
    /// carries no address, no credential and no customer text.
    /// </remarks>
    public string Reason { get; } = reason;
}
