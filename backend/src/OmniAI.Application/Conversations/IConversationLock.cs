namespace OmniAI.Application.Conversations;

/// <summary>
/// Serialises AI generation within one conversation.
/// </summary>
/// <remarks>
/// <strong>The problem this exists for.</strong> Answering a message means
/// reading the conversation so far, asking a provider for a reply, and appending
/// it. If two messages in the same conversation are answered at once, both read
/// the same history, both answer as though the other had not happened, and the
/// transcript ends up with two replies to the same state and an order nobody can
/// explain. Worse, each reply was paid for against a context that was already
/// stale when it was sent.
/// <para>
/// <strong>Why the scope is one conversation and not the process.</strong> Two
/// different conversations have nothing to say to each other, and serialising
/// them together would make every workspace's chat as slow as the slowest reply
/// anywhere on the deployment. The lock is taken per conversation so unrelated
/// visitors never wait for one another.
/// </para>
/// <para>
/// <strong>Why this is not a <c>SemaphoreSlim</c> in a static field.</strong> An
/// in-process lock is correct exactly until there are two processes, and then it
/// silently stops working — no error, no failed test, just interleaved
/// conversations under load. The platform will run more than one instance, so the
/// only honest place for this boundary is somewhere both instances can see, which
/// in this stack means the database.
/// </para>
/// <para>
/// <strong>Crash safety is part of the contract, not the implementation's
/// business.</strong> An implementation must ensure the hold is released if the
/// process holding it dies. A lock that can be left stuck after a crash would
/// take a conversation permanently out of service, and no amount of careful
/// <c>finally</c> blocks protects against a killed process.
/// </para>
/// </remarks>
public interface IConversationLock
{
    /// <summary>
    /// Waits for exclusive use of one conversation's generation path.
    /// </summary>
    /// <param name="conversationId">The conversation to serialise.</param>
    /// <param name="cancellationToken">
    /// Cancellation for the wait. A caller that has given up must not keep
    /// queueing behind a hold it no longer wants.
    /// </param>
    /// <returns>
    /// A hold. Disposing it releases the lock; letting the process die releases
    /// it too.
    /// </returns>
    Task<IAsyncDisposable> AcquireAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}
